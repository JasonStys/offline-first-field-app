// File: HttpSyncGateway.cs
// Purpose: Adapt the sync contract to bounded JSON HTTP requests and validated responses.
// Symbols and line locations: see docs/code-index.md.
// Important state: HttpClient owns base address, timeout, and transport resilience configuration.

using System.Net.Http.Json;
using FieldOps.Core;

namespace FieldOps.Infrastructure;

/// <summary>HTTP implementation of the remote synchronization boundary.</summary>
public sealed class HttpSyncGateway(HttpClient httpClient) : IRemoteSyncGateway
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<PushItemResult>> PushAsync(
        PushRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Mutations.Count is < 1 or > PushRequest.MaximumBatchSize)
        {
            throw new ArgumentOutOfRangeException(nameof(request));
        }

        using var response = await httpClient.PostAsJsonAsync(
                "/api/sync/push",
                request,
                cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<IReadOnlyList<PushItemResult>>(
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidDataException("Push response was empty.");
        if (result.Count != request.Mutations.Count)
        {
            throw new InvalidDataException("Push response count does not match the request count.");
        }

        return result;
    }

    /// <inheritdoc />
    public async Task<PullResponse> PullAsync(
        long afterCursor,
        int maximumCount,
        CancellationToken cancellationToken = default)
    {
        if (afterCursor < 0 || maximumCount is < 1 or > PullResponse.MaximumPullSize)
        {
            throw new ArgumentOutOfRangeException(nameof(afterCursor));
        }

        var response = await httpClient.GetFromJsonAsync<PullResponse>(
                $"/api/sync/pull?after={afterCursor}&limit={maximumCount}",
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidDataException("Pull response was empty.");
        if (response.NextCursor < afterCursor || response.Records.Count > maximumCount)
        {
            throw new InvalidDataException("Pull response violated cursor or size bounds.");
        }

        return response;
    }
}
