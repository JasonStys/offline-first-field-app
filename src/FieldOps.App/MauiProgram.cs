// File: MauiProgram.cs
// Purpose: Compose the MAUI client, offline store, bounded HTTP gateway, and dashboard dependencies.
// Symbols and line locations: see docs/code-index.md.
// Important settings: API base address is loopback-only and transport timeout is five seconds.

using FieldOps.Core;
using FieldOps.Infrastructure;
using Microsoft.Extensions.Logging;

namespace FieldOps.App;

/// <summary>MAUI dependency-composition root.</summary>
public static class MauiProgram
{
    /// <summary>Create the application service graph for Android or Windows.</summary>
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder().UseMauiApp<App>();
#if DEBUG
        builder.Logging.AddDebug();
#endif
        var launch = NativeLaunch.Read(Environment.GetCommandLineArgs());
        builder.Services.AddSingleton(launch);
        builder.Services.AddSingleton(_ => new NativeRuntime(launch.AcceptanceRoot is null
            ? FileSystem.AppDataDirectory : Path.Combine(launch.AcceptanceRoot, "profile")));
        builder.Services.AddSingleton(
            services => new LocalFieldStore(Path.Combine(services.GetRequiredService<NativeRuntime>().ProfileDirectory, "fieldops.db")));
        builder.Services.AddSingleton<ILocalFieldStore>(
            services => services.GetRequiredService<LocalFieldStore>());
        builder.Services.AddSingleton<IRemoteSyncGateway>(services => services.GetRequiredService<NativeRuntime>());
        builder.Services.AddSingleton<SyncCoordinator>();
        builder.Services.AddSingleton<DashboardViewModel>();
        builder.Services.AddSingleton<MainPage>();
        return builder.Build();
    }
}
