// File: Platforms/Android/MainActivity.cs
// Purpose: Host the shared MAUI surface in a single Android activity.
// Symbols and line locations: see docs/code-index.md.
// Important state: configuration changes preserve the shared application state.

using Android.App;
using Android.Content.PM;

namespace FieldOps.App;

[Activity(
    Theme = "@style/Maui.SplashTheme",
    MainLauncher = true,
    LaunchMode = LaunchMode.SingleTop,
    ConfigurationChanges = ConfigChanges.ScreenSize
        | ConfigChanges.Orientation
        | ConfigChanges.UiMode
        | ConfigChanges.ScreenLayout
        | ConfigChanges.SmallestScreenSize
        | ConfigChanges.Density)]
public sealed class MainActivity : MauiAppCompatActivity;
