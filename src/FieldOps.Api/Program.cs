// File: Program.cs
// Purpose: Configure the bounded ASP.NET Core reference synchronization service.
// Symbols and line locations: see docs/code-index.md.
// Important settings: request bodies are capped at one MiB and JSON rejects unknown members.

using FieldOps.Api;

var app = ApiBootstrap.Build(args);
app.Run();

public partial class Program;
