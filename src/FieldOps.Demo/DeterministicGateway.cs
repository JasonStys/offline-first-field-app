// File: DeterministicGateway.cs
// Purpose: Supply a network-free, deterministic remote boundary for the recruiter demo.
// Symbols and line locations: see docs/code-index.md.
// Important state: records is authoritative and cursor increases once per accepted mutation.

using FieldOps.Core;

namespace FieldOps.Demo;

/// <summary>Small in-process gateway that preserves production idempotency and version semantics.</summary>
public sealed class DeterministicGateway : IRemoteSyncGateway
{
    private readonly Dictionary<Guid, InspectionSnapshot> records = [];
    private readonly Dictionary<Guid, PushItemResult> results = [];
    private readonly List<(long Cursor, InspectionSnapshot Record)> changes = [];
    private long cursor;

    /// <inheritdoc />
    public Task<IReadOnlyList<PushItemResult>> PushAsync(
        PushRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var response = request.Mutations.Select(Apply).ToArray();
        return Task.FromResult<IReadOnlyList<PushItemResult>>(response);
    }

    /// <inheritdoc />
    public Task<PullResponse> PullAsync(
        long afterCursor,
        int maximumCount,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var page = changes.Where(change => change.Cursor > afterCursor).Take(maximumCount).ToArray();
        var next = page.Length == 0 ? afterCursor : page[^1].Cursor;
        return Task.FromResult(
            new PullResponse(next, page.Select(change => change.Record).ToArray()));
    }

    private PushItemResult Apply(InspectionMutation mutation)
    {
        if (results.TryGetValue(mutation.MutationId, out var cached))
        {
            return cached;
        }

        records.TryGetValue(mutation.Record.Id, out var current);
        if (current is not null && current.Version != mutation.ExpectedVersion)
        {
            return Cache(
                mutation.MutationId,
                new PushItemResult(mutation.MutationId, false, "conflict", null, current));
        }

        var accepted = mutation.Record with
        {
            Version = (current?.Version ?? 0) + 1,
            UpdatedAtUtc = DateTimeOffset.UnixEpoch.AddMinutes(cursor + 1),
        };
        records[accepted.Id] = accepted;
        changes.Add((++cursor, accepted));
        return Cache(
            mutation.MutationId,
            new PushItemResult(mutation.MutationId, true, "applied", accepted.Version, null));
    }

    private PushItemResult Cache(Guid mutationId, PushItemResult result)
    {
        results[mutationId] = result;
        return result;
    }
}
