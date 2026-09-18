// File: ApiEndpoints.cs
// Purpose: Map health, bounded push/pull, and redacted diagnostic HTTP contracts.
// Symbols and line locations: see docs/code-index.md.
// Important variables: store is singleton server state for the reference implementation.

using FieldOps.Core;

namespace FieldOps.Api;

/// <summary>Maps the complete public HTTP surface for the reference sync service.</summary>
public static class ApiEndpoints
{
    /// <summary>Register versioned synchronization routes and explicit validation responses.</summary>
    public static IEndpointRouteBuilder MapFieldOpsApi(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/healthz", () => Results.Ok(new { status = "healthy" }))
            .WithName("Health");

        endpoints.MapPost(
                "/api/sync/push",
                (PushRequest request, SyncServerStore store) =>
                {
                    if (request.Mutations.Count is < 1 or > PushRequest.MaximumBatchSize)
                    {
                        return Results.ValidationProblem(
                            new Dictionary<string, string[]>
                            {
                                ["mutations"] =
                                [
                                    $"Supply between 1 and {PushRequest.MaximumBatchSize} mutations.",
                                ],
                            });
                    }

                    return Results.Ok(store.Push(request));
                })
            .WithName("PushMutations");

        endpoints.MapGet(
                "/api/sync/pull",
                (long after, int limit, SyncServerStore store) =>
                {
                    if (after < 0 || limit is < 1 or > PullResponse.MaximumPullSize)
                    {
                        return Results.ValidationProblem(
                            new Dictionary<string, string[]>
                            {
                                ["cursor"] =
                                [
                                    $"after must be non-negative and limit must be 1-{PullResponse.MaximumPullSize}.",
                                ],
                            });
                    }

                    return Results.Ok(store.Pull(after, limit));
                })
            .WithName("PullDeltas");

        endpoints.MapGet(
                "/api/diagnostics",
                (SyncServerStore store) => Results.Ok(store.GetDiagnostics()))
            .WithName("Diagnostics");
        return endpoints;
    }
}
