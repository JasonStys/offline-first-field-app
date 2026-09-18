// File: ErrorHandling.cs
// Purpose: Convert malformed external JSON into safe client errors and redact unexpected failures.
// Symbols and line locations: see docs/code-index.md.
// Important state: response bodies never expose exception messages or stack traces.

using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics;

namespace FieldOps.Api;

/// <summary>Configures one production-safe exception boundary for the reference API.</summary>
public static class ErrorHandling
{
    /// <summary>Map bad request/JSON failures to 400 and every unexpected failure to 500.</summary>
    public static IApplicationBuilder UseFieldOpsExceptionHandler(this IApplicationBuilder app) =>
        app.UseExceptionHandler(
            handler =>
                handler.Run(
                    async context =>
                    {
                        var error = context.Features.Get<IExceptionHandlerFeature>()?.Error;
                        var isClientError = error is BadHttpRequestException or JsonException;
                        var statusCode = isClientError
                            ? StatusCodes.Status400BadRequest
                            : StatusCodes.Status500InternalServerError;
                        await Results.Problem(
                                statusCode: statusCode,
                                title: isClientError
                                    ? "Request payload is invalid."
                                    : "The server could not complete the request.")
                            .ExecuteAsync(context);
                    }));
}
