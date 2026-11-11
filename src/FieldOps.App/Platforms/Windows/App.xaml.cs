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
        var launch = NativeLaunch.Read(Environment.GetCommandLineArgs());
        if (launch.AcceptanceRoot is not null)
        {
            // Diagnostics belong only to this explicitly marked synthetic invocation, never AppData.
            void Record(Exception error)
            {
                try
                {
                    using var output = new FileStream(Path.Combine(launch.AcceptanceRoot,
                        "startup-failure-" + Environment.ProcessId + ".json"), FileMode.CreateNew);
                    System.Text.Json.JsonSerializer.Serialize(output, new
                    {
                        passed = false,
                        errorType = error.GetType().FullName,
                        message = error.Message,
                        stack = error.StackTrace,
                    });
                }
                catch (IOException) { /* An earlier exact failure remains authoritative. */ }
            }
            UnhandledException += (_, arguments) => Record(arguments.Exception);
            AppDomain.CurrentDomain.UnhandledException += (_, arguments) =>
            {
                if (arguments.ExceptionObject is Exception error) Record(error);
            };
        }
        InitializeComponent();
    }

    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
