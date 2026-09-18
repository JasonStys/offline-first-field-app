// File: LocalFieldStoreTests.cs
// Purpose: Exercise migrations, transactions, crash recovery, optimistic states, and safe search input.
// Symbols and line locations: see docs/code-index.md.
// Important fixture: TempWorkspace owns an isolated database and verifies cleanup boundaries.

using FieldOps.Core;
using FieldOps.Infrastructure;
using Microsoft.Data.Sqlite;

namespace FieldOps.Infrastructure.Tests;

public sealed class LocalFieldStoreTests
{
    [Fact]
    public async Task InitializeAndQueue_RoundTripsAndRecoversInterruptedWork()
    {
        using var workspace = new TempWorkspace();
        var store = new LocalFieldStore(workspace.DatabasePath);
        await store.InitializeAsync();
        await store.InitializeAsync();
        var record = CreateRecord("North bay");

        await store.SaveDraftAsync(record, "test-device");
        var firstBatch = await store.StartBatchAsync(10);
        var recovered = await store.RecoverInterruptedCommandsAsync();
        var secondBatch = await store.StartBatchAsync(10);

        Assert.Single(firstBatch);
        Assert.Equal(1, recovered);
        Assert.Single(secondBatch);
        Assert.Equal(firstBatch[0].Mutation.MutationId, secondBatch[0].Mutation.MutationId);
        Assert.Equal(2, secondBatch[0].Attempts);

        await store.MarkAppliedAsync(secondBatch[0].Mutation.MutationId, 3);
        var saved = Assert.Single(await store.SearchAsync("North", 10));
        Assert.Equal(3, saved.Version);
        Assert.Empty(await store.StartBatchAsync(10));
    }

    [Fact]
    public async Task ApplyRemote_DoesNotOverwriteRecordWithPendingLocalWork()
    {
        using var workspace = new TempWorkspace();
        var store = new LocalFieldStore(workspace.DatabasePath);
        await store.InitializeAsync();
        var local = CreateRecord("Local title");
        await store.SaveDraftAsync(local, "test-device");
        var remote = local with { Title = "Remote title", Version = 4 };

        await store.ApplyRemoteAsync([remote], 8);

        var result = Assert.Single(await store.SearchAsync(null, 10));
        Assert.Equal("Local title", result.Title);
        Assert.Equal(8, await store.GetCursorAsync());
    }

    [Fact]
    public async Task Search_TreatsGlobMetacharactersAsLiteralInput()
    {
        using var workspace = new TempWorkspace();
        var store = new LocalFieldStore(workspace.DatabasePath);
        await store.InitializeAsync();
        await store.SaveDraftAsync(CreateRecord("100* complete"), "test-device");
        await store.SaveDraftAsync(CreateRecord("1000 items"), "test-device");

        var results = await store.SearchAsync("100*", 10);

        Assert.Single(results);
        Assert.Equal("100* complete", results[0].Title);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(201)]
    public async Task Search_RejectsUnboundedLimits(int limit)
    {
        using var workspace = new TempWorkspace();
        var store = new LocalFieldStore(workspace.DatabasePath);
        await store.InitializeAsync();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => store.SearchAsync(null, limit));
    }

    [Fact]
    public async Task PrefixSearch_QueryPlanUsesTitleIndex()
    {
        using var workspace = new TempWorkspace();
        var store = new LocalFieldStore(workspace.DatabasePath);
        await store.InitializeAsync();
        await using var connection = new SqliteConnection(
            $"Data Source={workspace.DatabasePath};Pooling=False");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            EXPLAIN QUERY PLAN
            SELECT id, template_id, title, notes, status, version, updated_at_utc
            FROM inspections
            WHERE title GLOB $prefix
            ORDER BY updated_at_utc DESC, id
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$prefix", "North*");
        command.Parameters.AddWithValue("$limit", 10);

        var details = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            details.Add(reader.GetString(3));
        }

        Assert.Contains(details, detail => detail.Contains("ix_inspections_title", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SaveDraft_RejectsWorkWhenDurableQueueIsAtCapacity()
    {
        using var workspace = new TempWorkspace();
        var store = new LocalFieldStore(workspace.DatabasePath);
        await store.InitializeAsync();
        await using (var connection = new SqliteConnection(
            $"Data Source={workspace.DatabasePath};Pooling=False"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "UPDATE queue_guard SET total_count = 10000;";
            await command.ExecuteNonQueryAsync();
        }

        var exception = await Assert.ThrowsAsync<SqliteException>(
            () => store.SaveDraftAsync(CreateRecord("Capacity"), "test-device"));

        Assert.Contains("outbox capacity reached", exception.Message, StringComparison.Ordinal);
        Assert.Empty(await store.SearchAsync(null, 10));
    }

    [Fact]
    public async Task ConflictAndRejectedOutcomes_ArePersistedAndExcludedFromPendingBatch()
    {
        using var workspace = new TempWorkspace();
        var store = new LocalFieldStore(workspace.DatabasePath);
        await store.InitializeAsync();
        var conflictedRecord = CreateRecord("Conflict");
        var rejectedRecord = CreateRecord("Rejected");
        await store.SaveDraftAsync(conflictedRecord, "test-device");
        await store.SaveDraftAsync(rejectedRecord, "test-device");
        var batch = await store.StartBatchAsync(10);
        var conflict = batch.Single(item => item.Mutation.Record.Id == conflictedRecord.Id);
        var rejected = batch.Single(item => item.Mutation.Record.Id == rejectedRecord.Id);

        await store.MarkConflictAsync(
            conflict.Mutation.MutationId,
            conflictedRecord with { Version = 2, Notes = "Remote" });
        await store.MarkRejectedAsync(rejected.Mutation.MutationId, new string('x', 700));

        Assert.Empty(await store.StartBatchAsync(10));
    }

    private static InspectionSnapshot CreateRecord(string title) =>
        InspectionSnapshot.Create(
            Guid.NewGuid(),
            "fixture-v1",
            title,
            "Synthetic notes",
            InspectionStatus.Draft,
            0,
            DateTimeOffset.UnixEpoch).Value!;
}

internal sealed class TempWorkspace : IDisposable
{
    public TempWorkspace()
    {
        Root = Path.Combine(
            Path.GetTempPath(),
            "fieldops-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    public string DatabasePath => Path.Combine(Root, "fieldops.db");

    public void Dispose()
    {
        if (Root.StartsWith(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase))
        {
            Directory.Delete(Root, recursive: true);
        }
    }
}
