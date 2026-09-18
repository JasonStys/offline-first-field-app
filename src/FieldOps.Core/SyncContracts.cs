// File: SyncContracts.cs
// Purpose: Define bounded transport contracts and storage abstractions for offline synchronization.
// Symbols and line locations: see docs/code-index.md.
// Important limits: MaximumBatchSize and MaximumPullSize bound work and payload growth.

namespace FieldOps.Core;

/// <summary>Describes one idempotent optimistic-concurrency mutation.</summary>
public sealed record InspectionMutation(
    Guid MutationId,
    string DeviceId,
    long ExpectedVersion,
    InspectionSnapshot Record);

/// <summary>A durable local queue entry ordered by its monotonically increasing sequence.</summary>
public sealed record QueuedMutation(
    long Sequence,
    InspectionMutation Mutation,
    QueueState State,
    int Attempts,
    string? LastError);

/// <summary>Bounded push request sent by a client.</summary>
public sealed record PushRequest(IReadOnlyList<InspectionMutation> Mutations)
{
    public const int MaximumBatchSize = 100;
}

/// <summary>Outcome for one mutation; a conflict carries the current server snapshot.</summary>
public sealed record PushItemResult(
    Guid MutationId,
    bool Applied,
    string Code,
    long? AcceptedVersion,
    InspectionSnapshot? ServerRecord);

/// <summary>Ordered server deltas after an opaque numeric cursor.</summary>
public sealed record PullResponse(long NextCursor, IReadOnlyList<InspectionSnapshot> Records)
{
    public const int MaximumPullSize = 200;
}

/// <summary>One persisted conflict requiring explicit user review.</summary>
public sealed record ConflictRecord(
    Guid MutationId,
    InspectionSnapshot Local,
    InspectionSnapshot Remote,
    DateTimeOffset DetectedAtUtc);

/// <summary>Counts and status emitted after one bounded synchronization attempt.</summary>
public sealed record SyncReport(
    int Attempted,
    int Applied,
    int Conflicted,
    int Rejected,
    int Pulled,
    long Cursor);

/// <summary>Durable operations needed by the sync coordinator.</summary>
public interface ILocalFieldStore
{
    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task SaveDraftAsync(
        InspectionSnapshot record,
        string deviceId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<QueuedMutation>> StartBatchAsync(
        int maximumCount,
        CancellationToken cancellationToken = default);

    Task MarkAppliedAsync(
        Guid mutationId,
        long acceptedVersion,
        CancellationToken cancellationToken = default);

    Task MarkConflictAsync(
        Guid mutationId,
        InspectionSnapshot remote,
        CancellationToken cancellationToken = default);

    Task MarkRejectedAsync(
        Guid mutationId,
        string reason,
        CancellationToken cancellationToken = default);

    Task ReturnToPendingAsync(
        IReadOnlyCollection<Guid> mutationIds,
        string reason,
        CancellationToken cancellationToken = default);

    Task ApplyRemoteAsync(
        IReadOnlyList<InspectionSnapshot> records,
        long cursor,
        CancellationToken cancellationToken = default);

    Task<long> GetCursorAsync(CancellationToken cancellationToken = default);

    Task<int> RecoverInterruptedCommandsAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<InspectionSnapshot>> SearchAsync(
        string? query,
        int maximumCount,
        CancellationToken cancellationToken = default);
}

/// <summary>Remote boundary used by production HTTP and deterministic test gateways.</summary>
public interface IRemoteSyncGateway
{
    Task<IReadOnlyList<PushItemResult>> PushAsync(
        PushRequest request,
        CancellationToken cancellationToken = default);

    Task<PullResponse> PullAsync(
        long afterCursor,
        int maximumCount,
        CancellationToken cancellationToken = default);
}
