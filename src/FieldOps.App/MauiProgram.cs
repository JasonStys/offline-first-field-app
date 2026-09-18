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
        builder.Services.AddSingleton(
            _ => new LocalFieldStore(Path.Combine(FileSystem.AppDataDirectory, "fieldops.db")));
        builder.Services.AddSingleton<ILocalFieldStore>(
            services => services.GetRequiredService<LocalFieldStore>());
        builder.Services.AddSingleton(
            _ =>
            {
#if ANDROID
                var baseAddress = new Uri("https://10.0.2.2:7080", UriKind.Absolute);
#else
                var baseAddress = new Uri("http://127.0.0.1:5080", UriKind.Absolute);
#endif
                return new HttpClient { BaseAddress = baseAddress, Timeout = TimeSpan.FromSeconds(5) };
            });
        builder.Services.AddSingleton<IRemoteSyncGateway, HttpSyncGateway>();
        builder.Services.AddSingleton<SyncCoordinator>();
        builder.Services.AddSingleton<DashboardViewModel>();
        builder.Services.AddSingleton<MainPage>();
        return builder.Build();
    }
}
