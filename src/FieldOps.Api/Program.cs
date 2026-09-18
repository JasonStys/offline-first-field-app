// File: Program.cs
// Purpose: Configure the bounded ASP.NET Core reference synchronization service.
// Symbols and line locations: see docs/code-index.md.
// Important settings: request bodies are capped at one MiB and JSON rejects unknown members.

using System.Text.Json.Serialization;
using FieldOps.Api;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole();
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 1_048_576);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<SyncServerStore>();
builder.Services.AddProblemDetails();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.MaxDepth = 32;
    options.SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
});

var app = builder.Build();
app.UseFieldOpsExceptionHandler();
app.MapFieldOpsApi();
app.Run();

public partial class Program;
