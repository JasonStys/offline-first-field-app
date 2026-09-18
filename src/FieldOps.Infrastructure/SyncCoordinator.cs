// File: SyncCoordinator.cs
// Purpose: Coordinate crash recovery, bounded push, explicit result handling, and cursor-based pull.
// Symbols and line locations: see docs/code-index.md.
// Important variables: batchSize and pullSize bound each synchronization cycle.

using FieldOps.Core;

namespace FieldOps.Infrastructure;

/// <summary>Runs one deterministic synchronization cycle against abstract local and remote stores.</summary>
public sealed class SyncCoordinator(
    ILocalFieldStore localStore,
    IRemoteSyncGateway remoteGateway,
    int batchSize = 50,
    int pullSize = 100)
{
    private readonly int validatedBatchSize = ValidateSize(
        batchSize,
        PushRequest.MaximumBatchSize,
        nameof(batchSize));
    private readonly int validatedPullSize = ValidateSize(
        pullSize,
        PullResponse.MaximumPullSize,
        nameof(pullSize));

    /// <summary>
    /// Recover interrupted commands, push one bounded batch, then pull one bounded delta page.
    /// </summary>
    /// <remarks>Remote failures return in-flight work to pending before the exception is propagated.</remarks>
    public async Task<SyncReport> SynchronizeAsync(CancellationToken cancellationToken = default)
    {
        await localStore.RecoverInterruptedCommandsAsync(cancellationToken).ConfigureAwait(false);
        var batch = await localStore.StartBatchAsync(validatedBatchSize, cancellationToken)
            .ConfigureAwait(false);
        var applied = 0;
        var conflicted = 0;
        var rejected = 0;

        if (batch.Count > 0)
        {
            IReadOnlyList<PushItemResult> results;
            try
            {
                results = await remoteGateway.PushAsync(
                        new PushRequest(batch.Select(entry => entry.Mutation).ToArray()),
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                await localStore.ReturnToPendingAsync(
                        batch.Select(entry => entry.Mutation.MutationId).ToArray(),
                        exception.GetType().Name,
                        cancellationToken)
                    .ConfigureAwait(false);
                throw;
            }

            foreach (var result in results)
            {
                if (result.Applied && result.AcceptedVersion is { } acceptedVersion)
                {
                    await localStore.MarkAppliedAsync(
                            result.MutationId,
                            acceptedVersion,
                            cancellationToken)
                        .ConfigureAwait(false);
                    applied++;
                }
                else if (string.Equals(result.Code, "conflict", StringComparison.Ordinal)
                         && result.ServerRecord is not null)
                {
                    await localStore.MarkConflictAsync(
                            result.MutationId,
                            result.ServerRecord,
                            cancellationToken)
                        .ConfigureAwait(false);
                    conflicted++;
                }
                else
                {
                    await localStore.MarkRejectedAsync(
                            result.MutationId,
                            result.Code,
                            cancellationToken)
                        .ConfigureAwait(false);
                    rejected++;
                }
            }
        }

        var cursor = await localStore.GetCursorAsync(cancellationToken).ConfigureAwait(false);
        var pulled = await remoteGateway.PullAsync(cursor, validatedPullSize, cancellationToken)
            .ConfigureAwait(false);
        await localStore.ApplyRemoteAsync(pulled.Records, pulled.NextCursor, cancellationToken)
            .ConfigureAwait(false);
        return new SyncReport(
            batch.Count,
            applied,
            conflicted,
            rejected,
            pulled.Records.Count,
            pulled.NextCursor);
    }

    private static int ValidateSize(int value, int maximum, string parameterName) =>
        value is >= 1 && value <= maximum
            ? value
            : throw new ArgumentOutOfRangeException(parameterName);
}
