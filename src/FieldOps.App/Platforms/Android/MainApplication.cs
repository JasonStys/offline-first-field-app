// File: Platforms/Android/MainApplication.cs
// Purpose: Bootstrap the shared MAUI dependency graph on Android.
// Symbols and line locations: see docs/code-index.md.
// Important state: Android owns the application lifetime and native handle.

using Android.App;
using Android.Runtime;

namespace FieldOps.App;

[Application]
public sealed class MainApplication(IntPtr handle, JniHandleOwnership ownership)
    : MauiApplication(handle, ownership)
{
    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
