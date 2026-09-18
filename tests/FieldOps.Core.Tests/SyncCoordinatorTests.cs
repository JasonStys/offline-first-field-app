// File: SyncCoordinatorTests.cs
// Purpose: Verify bounded orchestration, explicit result routing, pull application, and retry recovery.
// Symbols and line locations: see docs/code-index.md.
// Important fixtures: FakeLocalStore records every coordinator transition for contract assertions.

using FieldOps.Core;
using FieldOps.Infrastructure;

namespace FieldOps.Core.Tests;

public sealed class SyncCoordinatorTests
{
    [Fact]
    public async Task SynchronizeAsync_RoutesEveryResultAndAppliesPull()
    {
        var queued = CreateQueuedMutations(3);
        var local = new FakeLocalStore(queued);
        var remote = new FakeGateway(
            [
                new(queued[0].Mutation.MutationId, true, "applied", 1, null),
                new(queued[1].Mutation.MutationId, false, "conflict", null, queued[1].Mutation.Record),
                new(queued[2].Mutation.MutationId, false, "invalid_record", null, null),
            ],
            new PullResponse(4, [queued[0].Mutation.Record with { Version = 1 }]));

        var report = await new SyncCoordinator(local, remote, 10, 10).SynchronizeAsync();

        Assert.Equal(new SyncReport(3, 1, 1, 1, 1, 4), report);
        Assert.Single(local.Applied);
        Assert.Single(local.Conflicted);
        Assert.Single(local.Rejected);
        Assert.Equal(4, local.AppliedCursor);
    }

    [Fact]
    public async Task SynchronizeAsync_ReturnsInFlightCommandsToPendingAfterTransportFailure()
    {
        var queued = CreateQueuedMutations(2);
        var local = new FakeLocalStore(queued);
        var remote = new FakeGateway([], new PullResponse(0, []), shouldFail: true);
        var coordinator = new SyncCoordinator(local, remote);

        await Assert.ThrowsAsync<HttpRequestException>(() => coordinator.SynchronizeAsync());

        Assert.Equal(
            queued.Select(entry => entry.Mutation.MutationId),
            local.ReturnedToPending);
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(101, 100)]
    [InlineData(10, 201)]
    public void Constructor_RejectsUnboundedPageSizes(int batchSize, int pullSize)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new SyncCoordinator(
                new FakeLocalStore([]),
                new FakeGateway([], new PullResponse(0, [])),
                batchSize,
                pullSize));
    }

    private static IReadOnlyList<QueuedMutation> CreateQueuedMutations(int count) =>
        Enumerable.Range(0, count)
            .Select(
                index =>
                {
                    var record = InspectionSnapshot.Create(
                        Guid.NewGuid(),
                        "template",
                        $"Record {index}",
                        string.Empty,
                        InspectionStatus.Draft,
                        0,
                        DateTimeOffset.UnixEpoch).Value!;
                    var mutation = new InspectionMutation(Guid.NewGuid(), "device", 0, record);
                    return new QueuedMutation(index + 1, mutation, QueueState.Pending, 0, null);
                })
            .ToArray();

    private sealed class FakeGateway(
        IReadOnlyList<PushItemResult> pushResults,
        PullResponse pullResponse,
        bool shouldFail = false) : IRemoteSyncGateway
    {
        public Task<IReadOnlyList<PushItemResult>> PushAsync(
            PushRequest request,
            CancellationToken cancellationToken = default) =>
            shouldFail
                ? Task.FromException<IReadOnlyList<PushItemResult>>(
                    new HttpRequestException("Synthetic outage"))
                : Task.FromResult(pushResults);

        public Task<PullResponse> PullAsync(
            long afterCursor,
            int maximumCount,
            CancellationToken cancellationToken = default) => Task.FromResult(pullResponse);
    }

    private sealed class FakeLocalStore(IReadOnlyList<QueuedMutation> entries) : ILocalFieldStore
    {
        public List<Guid> Applied { get; } = [];
        public List<Guid> Conflicted { get; } = [];
        public List<Guid> Rejected { get; } = [];
        public List<Guid> ReturnedToPending { get; } = [];
        public long AppliedCursor { get; private set; }

        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SaveDraftAsync(InspectionSnapshot record, string deviceId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<QueuedMutation>> StartBatchAsync(int maximumCount, CancellationToken cancellationToken = default) => Task.FromResult(entries);
        public Task MarkAppliedAsync(Guid mutationId, long acceptedVersion, CancellationToken cancellationToken = default) { Applied.Add(mutationId); return Task.CompletedTask; }
        public Task MarkConflictAsync(Guid mutationId, InspectionSnapshot remote, CancellationToken cancellationToken = default) { Conflicted.Add(mutationId); return Task.CompletedTask; }
        public Task MarkRejectedAsync(Guid mutationId, string reason, CancellationToken cancellationToken = default) { Rejected.Add(mutationId); return Task.CompletedTask; }
        public Task ReturnToPendingAsync(IReadOnlyCollection<Guid> mutationIds, string reason, CancellationToken cancellationToken = default) { ReturnedToPending.AddRange(mutationIds); return Task.CompletedTask; }
        public Task ApplyRemoteAsync(IReadOnlyList<InspectionSnapshot> records, long cursor, CancellationToken cancellationToken = default) { AppliedCursor = cursor; return Task.CompletedTask; }
        public Task<long> GetCursorAsync(CancellationToken cancellationToken = default) => Task.FromResult(0L);
        public Task<int> RecoverInterruptedCommandsAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task<IReadOnlyList<InspectionSnapshot>> SearchAsync(string? query, int maximumCount, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<InspectionSnapshot>>([]);
    }
}
