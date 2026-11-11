// File: ApiBootstrap.cs
// Purpose: Share the real bounded synchronization API between CLI hosting and an owned native companion.
// Symbols and line locations: see docs/code-index.md.
// State: native mode has one fixed loopback listener and an ephemeral private request capability.

using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace FieldOps.Api;

/// <summary>Original endpoint composition with an explicit isolated native-owner mode.</summary>
public static partial class ApiBootstrap
{
    /// <summary>Build the same domain endpoints; native storage/token never originate in HTTP input.</summary>
    public static WebApplication Build(string[] args, SyncServerStore? nativeStore = null, string? nativeToken = null)
    {
        var native = nativeStore is not null || nativeToken is not null;
        if (native && (nativeStore is null || nativeToken is null || !TokenPattern().IsMatch(nativeToken)))
            throw new ArgumentException("Native hosting requires both an owned store and fixed-size capability.");
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = native ? [] : args,
            ApplicationName = typeof(ApiBootstrap).Assembly.GetName().Name,
            ContentRootPath = native ? AppContext.BaseDirectory : null,
        });
        builder.Logging.ClearProviders();
        if (native)
        {
            builder.Configuration.Sources.Clear();
            builder.WebHost.ConfigureKestrel(options =>
            {
                options.Listen(IPAddress.Loopback, 0);
                options.Limits.MaxConcurrentConnections = 16;
                options.Limits.MaxRequestBodySize = 1_048_576;
                options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(5);
                options.Limits.KeepAliveTimeout = TimeSpan.FromSeconds(2);
            });
        }
        else
        {
            builder.Logging.AddJsonConsole();
            builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 1_048_576);
        }
        builder.Services.AddSingleton(TimeProvider.System);
        if (nativeStore is not null) builder.Services.AddSingleton(nativeStore);
        else builder.Services.AddSingleton<SyncServerStore>();
        builder.Services.AddProblemDetails();
        builder.Services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.MaxDepth = 32;
            options.SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
            options.SerializerOptions.AllowDuplicateProperties = false;
        });
        var app = builder.Build();
        app.UseFieldOpsExceptionHandler();
        if (native)
        {
            var expected = Encoding.ASCII.GetBytes(nativeToken!);
            app.Use(async (context, next) =>
            {
                var authority = "127.0.0.1:" + context.Connection.LocalPort;
                var origin = "http://" + authority;
                if (!string.Equals(context.Request.Host.Value, authority, StringComparison.Ordinal)
                    || (context.Request.Headers.ContainsKey("Origin")
                        && !string.Equals(context.Request.Headers.Origin, origin, StringComparison.Ordinal)))
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return;
                }
                var supplied = context.Request.Headers["X-FieldOps-Native"];
                if (supplied.Count != 1 || !TokenPattern().IsMatch(supplied[0] ?? "")
                    || !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(supplied[0]!), expected))
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return;
                }
                context.Response.Headers.CacheControl = "no-store";
                context.Response.Headers.XContentTypeOptions = "nosniff";
                await next(context);
            });
        }
        app.MapFieldOpsApi();
        return app;
    }

    [GeneratedRegex("^[a-f0-9]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex TokenPattern();
}
