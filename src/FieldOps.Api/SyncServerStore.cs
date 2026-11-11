// File: SyncServerStore.cs
// Purpose: Provide a deterministic, thread-safe reference server for idempotent optimistic sync.
// Symbols and line locations: see docs/code-index.md.
// Important state: records is authoritative; results caches idempotency; changes assigns cursors.

using FieldOps.Core;

namespace FieldOps.Api;

/// <summary>Thread-safe in-memory reference implementation of the server synchronization rules.</summary>
public sealed class SyncServerStore
{
    private readonly TimeProvider timeProvider;
    private readonly NativeCheckpoint? checkpoint;
    private readonly Lock gate = new();
    private readonly Dictionary<Guid, InspectionSnapshot> records = [];
    private readonly Dictionary<Guid, PushItemResult> results = [];
    private readonly List<(long Cursor, InspectionSnapshot Record)> changes = [];
    private long nextCursor;

    /// <summary>Keep the original in-memory default; native composition can explicitly retain state.</summary>
    public SyncServerStore(TimeProvider timeProvider, NativeCheckpoint? checkpoint = null)
    {
        this.timeProvider = timeProvider;
        this.checkpoint = checkpoint;
        var retained = checkpoint?.Load();
        if (retained is not null) Restore(retained);
    }

    /// <summary>
    /// Apply a bounded batch in order, replaying cached results for duplicate mutation identifiers.
    /// </summary>
    /// <remarks>The volatile default is O(m) expected; native mode also copies/validates/serializes its bounded full checkpoint under the gate.</remarks>
    public IReadOnlyList<PushItemResult> Push(PushRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Mutations.Count is < 1 or > PushRequest.MaximumBatchSize)
        {
            throw new ArgumentOutOfRangeException(nameof(request));
        }

        lock (gate)
        {
            var before = checkpoint is null ? null : Capture();
            try
            {
                var result = request.Mutations.Select(ApplyMutation).ToArray();
                checkpoint?.Save(Capture());
                return result;
            }
            catch
            {
                if (before is not null) Restore(before);
                throw;
            }
        }
    }

    /// <summary>Return ordered changes after a cursor, bounded by the requested page size.</summary>
    public PullResponse Pull(long afterCursor, int maximumCount)
    {
        if (afterCursor < 0 || maximumCount is < 1 or > PullResponse.MaximumPullSize)
        {
            throw new ArgumentOutOfRangeException(nameof(afterCursor));
        }

        lock (gate)
        {
            var page = changes
                .Where(change => change.Cursor > afterCursor)
                .Take(maximumCount)
                .ToArray();
            var cursor = page.Length == 0 ? afterCursor : page[^1].Cursor;
            return new PullResponse(cursor, page.Select(change => change.Record).ToArray());
        }
    }

    /// <summary>Return non-sensitive counts for the local diagnostics endpoint.</summary>
    public ServerDiagnostics GetDiagnostics()
    {
        lock (gate)
        {
            return new ServerDiagnostics(records.Count, results.Count, changes.Count, nextCursor);
        }
    }

    /// <summary>Native shutdown retains ownership until every already-started store operation completes.</summary>
    public void WaitForIdle()
    {
        lock (gate) { }
    }

    private NativeServerState Capture() => new(1, records.Values.ToArray(), results.Values.ToArray(),
        changes.Select(item => new NativeServerChange(item.Cursor, item.Record)).ToArray(), nextCursor);

    private void Restore(NativeServerState state)
    {
        records.Clear();
        results.Clear();
        changes.Clear();
        foreach (var record in state.Records) records.Add(record.Id, record);
        foreach (var result in state.Results) results.Add(result.MutationId, result);
        changes.AddRange(state.Changes.Select(item => (item.Cursor, item.Record)));
        nextCursor = state.NextCursor;
    }

    private PushItemResult ApplyMutation(InspectionMutation mutation)
    {
        if (results.TryGetValue(mutation.MutationId, out var cached))
        {
            return cached;
        }

        var validation = ValidateMutation(mutation);
        if (validation is not null)
        {
            results[mutation.MutationId] = validation;
            return validation;
        }

        records.TryGetValue(mutation.Record.Id, out var current);
        if (current is not null && current.Version != mutation.ExpectedVersion)
        {
            var conflict = new PushItemResult(
                mutation.MutationId,
                false,
                "conflict",
                null,
                current);
            results[mutation.MutationId] = conflict;
            return conflict;
        }

        if (current is null && mutation.ExpectedVersion != 0)
        {
            var missing = new PushItemResult(
                mutation.MutationId,
                false,
                "unknown_record_version",
                null,
                null);
            results[mutation.MutationId] = missing;
            return missing;
        }

        var accepted = mutation.Record with
        {
            Version = (current?.Version ?? 0) + 1,
            UpdatedAtUtc = timeProvider.GetUtcNow(),
        };
        records[accepted.Id] = accepted;
        changes.Add((++nextCursor, accepted));
        var applied = new PushItemResult(
            mutation.MutationId,
            true,
            "applied",
            accepted.Version,
            null);
        results[mutation.MutationId] = applied;
        return applied;
    }

    private static PushItemResult? ValidateMutation(InspectionMutation mutation)
    {
        if (mutation.MutationId == Guid.Empty
            || string.IsNullOrWhiteSpace(mutation.DeviceId)
            || mutation.DeviceId.Length > 64
            || mutation.ExpectedVersion < 0)
        {
            return new PushItemResult(
                mutation.MutationId,
                false,
                "invalid_mutation",
                null,
                null);
        }

        var record = mutation.Record;
        var validation = InspectionSnapshot.Create(
            record.Id,
            record.TemplateId,
            record.Title,
            record.Notes,
            record.Status,
            record.Version,
            record.UpdatedAtUtc);
        return validation.IsValid
            ? null
            : new PushItemResult(
                mutation.MutationId,
                false,
                "invalid_record",
                null,
                null);
    }
}

/// <summary>Non-sensitive operational counters for a development sync server.</summary>
public sealed record ServerDiagnostics(
    int RecordCount,
    int IdempotencyEntryCount,
    int ChangeCount,
    long CurrentCursor);
