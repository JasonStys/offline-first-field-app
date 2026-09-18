// File: Platforms/Windows/App.xaml.cs
// Purpose: Bootstrap the MAUI application on Windows through WinUI.
// Symbols and line locations: see docs/code-index.md.
// Important state: no platform-specific business state is retained here.

namespace FieldOps.App.WinUI;

/// <summary>Windows application host.</summary>
public partial class App : MauiWinUIApplication
{
    public App()
    {
        InitializeComponent();
    }

    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
