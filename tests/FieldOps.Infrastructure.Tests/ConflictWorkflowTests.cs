// File: ConflictWorkflowTests.cs
// Purpose: Exercise actual SQLite transactions and the portable MAUI view model as an offline reviewer.
// Symbols and line locations: see docs/code-index.md.
// Important fixtures: each test owns its database; gateway models independent server CAS, never real devices.

using FieldOps.App;
using FieldOps.Core;
using FieldOps.Infrastructure;
using Microsoft.Data.Sqlite;

namespace FieldOps.Infrastructure.Tests;

public sealed class ConflictWorkflowTests
{
    [Fact]
    public async Task FieldReviewer_CancelsThenChoosesAndSynchronizesWithoutLosingEitherWritersChanges()
    {
        using var workspace = new TempWorkspace();
        var store = new LocalFieldStore(workspace.DatabasePath);
        await store.InitializeAsync();
        var baseline = Record();
        await store.ApplyRemoteAsync([baseline], 1);
        var local = baseline with { Title = "Pump housing checked locally" };
        var remote = baseline with { Title = "Supervisor inspection", Notes = "Seal replaced remotely", Status = InspectionStatus.ReadyForReview, Version = 4 };
        await store.SaveDraftAsync(local, "field-device");
        var gateway = new VersionedGateway(remote);
        var coordinator = new SyncCoordinator(store, gateway);
        await coordinator.SynchronizeAsync();
        var viewModel = new DashboardViewModel(store, coordinator);
        await viewModel.InitializeAsync();
        var review = Assert.Single(viewModel.Conflicts);
        Assert.Equal(baseline, review.Baseline);
        viewModel.SelectedConflict = review;
        Assert.Equal(-1, Field(viewModel, "Title").SelectedIndex);
        Assert.Equal(1, Field(viewModel, "Notes").SelectedIndex);

        await viewModel.ResolveSelectedConflictAsync();
        Assert.Contains("Choose", viewModel.StatusText, StringComparison.Ordinal);
        Assert.Single(await store.GetConflictsAsync());
        viewModel.CancelConflictReview();
        Assert.False(viewModel.HasConflictReview);
        Assert.Single(await store.GetConflictsAsync());

        viewModel.SelectedConflict = review;
        Field(viewModel, "Title").SelectedIndex = 0;
        await viewModel.ResolveSelectedConflictAsync();
        Assert.Empty(viewModel.Conflicts);
        Assert.Equal(1, gateway.PushCount); // Resolution itself is entirely local.
        var pending = Assert.Single(await store.StartBatchAsync(10));
        Assert.NotEqual(review.MutationId, pending.Mutation.MutationId);
        Assert.Equal(4, pending.Mutation.ExpectedVersion);
        Assert.Equal(local.Title, pending.Mutation.Record.Title);
        Assert.Equal(remote.Notes, pending.Mutation.Record.Notes);
        Assert.Equal(remote.Status, pending.Mutation.Record.Status);

        await viewModel.SynchronizeAsync(); // Recovers the test's inspected in-flight command.
        Assert.Empty(await store.GetConflictsAsync());
        var saved = Assert.Single(viewModel.Inspections);
        Assert.Equal(5, saved.Version);
        Assert.Equal(local.Title, saved.Title);
        Assert.Equal(remote.Notes, saved.Notes);
    }

    [Fact]
    public async Task UnknownBaseline_DoesNotInventPreviousValuesAndAllowsExplicitEmptyNotes()
    {
        using var workspace = new TempWorkspace();
        var store = new LocalFieldStore(workspace.DatabasePath);
        await store.InitializeAsync();
        var local = Record() with { Version = 0, Notes = string.Empty };
        await store.SaveDraftAsync(local, "field-device");
        var command = Assert.Single(await store.StartBatchAsync(10));
        await store.MarkConflictAsync(command.Mutation.MutationId, local with { Version = 2, Notes = "Remote note" });
        var review = Assert.Single(await store.GetConflictsAsync());
        Assert.Null(review.Baseline);
        var choices = All(ResolutionSide.Local);
        await store.ResolveConflictAsync(review, choices, "field-device");
        var replacement = Assert.Single(await store.StartBatchAsync(10));
        Assert.Empty(replacement.Mutation.Record.Notes);
        Assert.Equal(2, replacement.Mutation.ExpectedVersion);
    }

    [Fact]
    public async Task StaleReview_IsRefusedAndViewModelReloadsCurrentEvidenceWithoutOverwriting()
    {
        using var workspace = new TempWorkspace();
        var store = new LocalFieldStore(workspace.DatabasePath);
        await store.InitializeAsync();
        var local = Record() with { Version = 0 };
        await store.SaveDraftAsync(local, "field-device");
        var command = Assert.Single(await store.StartBatchAsync(10));
        var remote = local with { Version = 2, Title = "Remote two" };
        await store.MarkConflictAsync(command.Mutation.MutationId, remote);
        var viewModel = new DashboardViewModel(store, new SyncCoordinator(store, new VersionedGateway(remote)));
        await viewModel.InitializeAsync();
        viewModel.SelectedConflict = Assert.Single(viewModel.Conflicts);
        foreach (var field in viewModel.ConflictFields) field.SelectedIndex = 0;
        await store.MarkConflictAsync(command.Mutation.MutationId, remote with { Version = 3, Title = "Remote three" });
        await viewModel.ResolveSelectedConflictAsync();
        Assert.Contains("changed", viewModel.StatusText, StringComparison.Ordinal);
        Assert.False(viewModel.HasConflictReview);
        Assert.Equal(3, Assert.Single(viewModel.Conflicts).Remote.Version);
        Assert.Empty(await store.StartBatchAsync(10));
        Assert.Equal(local.Title, Assert.Single(await store.SearchAsync(null, 10)).Title);
    }

    [Fact]
    public async Task RemoteChangeAfterResolution_BecomesANewReviewWithTheCorrectBase()
    {
        using var workspace = new TempWorkspace();
        var store = new LocalFieldStore(workspace.DatabasePath);
        await store.InitializeAsync();
        var local = Record() with { Version = 0 };
        await store.SaveDraftAsync(local, "field-device");
        var command = Assert.Single(await store.StartBatchAsync(10));
        var remote = local with { Version = 2, Notes = "Remote two" };
        await store.MarkConflictAsync(command.Mutation.MutationId, remote);
        var old = Assert.Single(await store.GetConflictsAsync());
        var replacement = await store.ResolveConflictAsync(old, All(ResolutionSide.Local), "field-device");
        await new SyncCoordinator(store, new VersionedGateway(remote with { Version = 3, Notes = "Remote three" })).SynchronizeAsync();
        var next = Assert.Single(await store.GetConflictsAsync());
        Assert.Equal(replacement, next.MutationId);
        Assert.Equal(remote, next.Baseline);
        Assert.Equal(local.Notes, next.Local.Notes);
        Assert.Equal(3, next.Remote.Version);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ResolveConflictAsync(old, All(ResolutionSide.Local), "field-device"));
    }

    [Fact]
    public async Task ExistingV1PendingWork_MigratesWithoutFabricatingABaseline()
    {
        using var workspace = new TempWorkspace();
        var store = new LocalFieldStore(workspace.DatabasePath);
        await store.InitializeAsync();
        var local = Record();
        await store.SaveDraftAsync(local, "field-device");
        await using (var connection = new SqliteConnection($"Data Source={workspace.DatabasePath};Pooling=False"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "DROP TABLE mutation_baselines; DELETE FROM schema_history WHERE version = 2;";
            await command.ExecuteNonQueryAsync();
        }

        await store.InitializeAsync();
        await store.InitializeAsync();
        var mutation = Assert.Single(await store.StartBatchAsync(10));
        await store.MarkConflictAsync(mutation.Mutation.MutationId, local with { Version = 4 });
        Assert.Null(Assert.Single(await store.GetConflictsAsync()).Baseline);
    }

    [Fact]
    public async Task DuplicateDraftAndWrongIdentity_AreRejectedWithoutDamagingPendingEvidence()
    {
        using var workspace = new TempWorkspace();
        var store = new LocalFieldStore(workspace.DatabasePath);
        await store.InitializeAsync();
        var local = Record();
        await store.SaveDraftAsync(local, "field-device");
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveDraftAsync(local with { Title = "Lost edit" }, "field-device"));
        var command = Assert.Single(await store.StartBatchAsync(10));
        await Assert.ThrowsAsync<ArgumentException>(() => store.MarkConflictAsync(command.Mutation.MutationId, local with { Id = Guid.NewGuid(), Version = 4 }));
        Assert.Empty(await store.GetConflictsAsync());
        Assert.Equal(local.Title, Assert.Single(await store.SearchAsync(null, 10)).Title);
    }

    [Fact]
    public void ResolutionPolicy_RejectsMissingUnknownAndInvalidChoicesAndMismatchedEvidence()
    {
        var local = Record();
        var review = new ConflictReview(Guid.NewGuid(), null, local, local with { Version = 4 }, DateTimeOffset.UnixEpoch);
        Assert.Throws<ArgumentException>(() => ConflictResolutionPolicy.Resolve(review, new Dictionary<string, ResolutionSide>(), DateTimeOffset.UnixEpoch));
        var invalid = All(ResolutionSide.Local);
        invalid["Title"] = (ResolutionSide)42;
        Assert.Throws<ArgumentException>(() => ConflictResolutionPolicy.Resolve(review, invalid, DateTimeOffset.UnixEpoch));
        Assert.Throws<ArgumentException>(() => ConflictResolutionPolicy.Resolve(review with { Remote = review.Remote with { Id = Guid.NewGuid() } }, All(ResolutionSide.Local), DateTimeOffset.UnixEpoch));
        Assert.Throws<ArgumentException>(() => ConflictResolutionPolicy.Resolve(review with { Local = local with { Title = string.Empty } }, All(ResolutionSide.Local), DateTimeOffset.UnixEpoch));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(201)]
    public async Task ConflictList_IsBounded(int limit)
    {
        using var workspace = new TempWorkspace();
        var store = new LocalFieldStore(workspace.DatabasePath);
        await store.InitializeAsync();
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => store.GetConflictsAsync(limit));
    }

    [Fact]
    public async Task FailedReplacementInsert_RollsBackRemovedConflictAndOriginalValues()
    {
        using var workspace = new TempWorkspace();
        var store = new LocalFieldStore(workspace.DatabasePath);
        await store.InitializeAsync();
        var local = Record();
        await store.SaveDraftAsync(local, "field-device");
        var mutation = Assert.Single(await store.StartBatchAsync(10));
        await store.MarkConflictAsync(mutation.Mutation.MutationId, local with { Version = 4, Title = "Remote edit" });
        var review = Assert.Single(await store.GetConflictsAsync());
        await using (var connection = new SqliteConnection($"Data Source={workspace.DatabasePath};Pooling=False"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TRIGGER reject_test_replacement BEFORE INSERT ON outbox_commands
                WHEN NEW.device_id = 'fail-test' BEGIN SELECT RAISE(ABORT, 'simulated insert failure'); END;
                """;
            await command.ExecuteNonQueryAsync();
        }

        await Assert.ThrowsAsync<SqliteException>(() => store.ResolveConflictAsync(review, All(ResolutionSide.Remote), "fail-test"));
        Assert.Equal(review, Assert.Single(await store.GetConflictsAsync()));
        Assert.Equal(local, Assert.Single(await store.SearchAsync(null, 10)));
        Assert.Empty(await store.StartBatchAsync(10));
        await store.ResolveConflictAsync(review, All(ResolutionSide.Remote), "field-device");
        Assert.Single(await store.StartBatchAsync(10));
    }

    [Fact]
    public async Task CancelledResolution_AndInvalidDevice_KeepTheConflictAvailable()
    {
        using var workspace = new TempWorkspace();
        var store = new LocalFieldStore(workspace.DatabasePath);
        await store.InitializeAsync();
        var local = Record();
        await store.SaveDraftAsync(local, "field-device");
        var mutation = Assert.Single(await store.StartBatchAsync(10));
        await store.MarkConflictAsync(mutation.Mutation.MutationId, local with { Version = 4 });
        var review = Assert.Single(await store.GetConflictsAsync());
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => store.ResolveConflictAsync(review, All(ResolutionSide.Local), " "));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.ResolveConflictAsync(review, All(ResolutionSide.Local), "field-device", cancellation.Token));
        Assert.Equal(review, Assert.Single(await store.GetConflictsAsync()));
        Assert.Empty(await store.StartBatchAsync(10));
    }

    [Theory]
    [InlineData("base", "base", "remote", 1)]
    [InlineData("base", "local", "base", 0)]
    [InlineData("base", "same", "same", 1)]
    [InlineData("base", "local", "remote", -1)]
    [InlineData(null, "local", "remote", -1)]
    [InlineData(null, "same", "same", 1)]
    public void ThreeWayRecommendations_AreConservative(string? baseline, string local, string remote, int expected)
    {
        var field = new ConflictFieldChoice("Title", baseline, local, remote);
        Assert.Equal(expected, field.SelectedIndex);
        if (expected < 0) Assert.Throws<ArgumentException>(() => field.GetSelection());
        field.SelectedIndex = 0;
        Assert.Equal(ResolutionSide.Local, field.GetSelection());
        Assert.Throws<ArgumentOutOfRangeException>(() => field.SelectedIndex = 2);
    }

    private static ConflictFieldChoice Field(DashboardViewModel model, string name) => model.ConflictFields.Single(field => field.Name == name);
    private static Dictionary<string, ResolutionSide> All(ResolutionSide side) => ConflictResolutionPolicy.FieldNames.ToDictionary(field => field, _ => side, StringComparer.Ordinal);
    private static InspectionSnapshot Record() => InspectionSnapshot.Create(Guid.NewGuid(), "pump-v1", "Pump inspection", "Original notes", InspectionStatus.Draft, 3, DateTimeOffset.UnixEpoch).Value!;

    private sealed class VersionedGateway(InspectionSnapshot current) : IRemoteSyncGateway
    {
        private InspectionSnapshot server = current;
        public int PushCount { get; private set; }

        public Task<IReadOnlyList<PushItemResult>> PushAsync(PushRequest request, CancellationToken cancellationToken = default)
        {
            PushCount++;
            var results = new List<PushItemResult>();
            foreach (var item in request.Mutations)
            {
                if (item.ExpectedVersion != server.Version)
                {
                    results.Add(new(item.MutationId, false, "conflict", null, server));
                }
                else
                {
                    server = item.Record with { Version = server.Version + 1 };
                    results.Add(new(item.MutationId, true, "applied", server.Version, server));
                }
            }

            return Task.FromResult<IReadOnlyList<PushItemResult>>(results);
        }

        public Task<PullResponse> PullAsync(long afterCursor, int maximumCount, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PullResponse(server.Version, [server]));
    }
}
