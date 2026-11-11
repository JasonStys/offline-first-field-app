// File: App.xaml.cs
// Purpose: Create the single MAUI window from the dependency-injected field dashboard.
// Symbols and line locations: see docs/code-index.md.
// Important state: mainPage is resolved once and owned by the application window.

namespace FieldOps.App;

/// <summary>Application root for Android and Windows targets.</summary>
public partial class App : Application
{
    private readonly IServiceProvider services;

    public App(IServiceProvider services)
    {
        InitializeComponent();
        this.services = services;
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        // Resolve resource-consuming pages only after the actual Application resources exist.
        var mainPage = services.GetRequiredService<MainPage>();
        var window = new Window(mainPage)
        {
            Title = "FieldOps Offline 1.1.0 · Jason Stys",
            Width = 1280,
            Height = 900,
            MinimumWidth = 1050,
            MinimumHeight = 720,
        };
        mainPage.RequestQuitAsync = async () => { await mainPage.CloseAsync(); CloseWindow(window); };
#if WINDOWS
        var completed = false;
        var waiting = false;
        window.Created += (_, _) =>
        {
            var native = (Microsoft.UI.Xaml.Window)window.Handler!.PlatformView!;
            async Task CloseOwnedAsync()
            {
                if (waiting) return;
                waiting = true;
                try
                {
                    await mainPage.CloseAsync();
                    completed = true;
                    native.Close();
                }
                catch (Exception error) when (error is IOException or InvalidOperationException or UnauthorizedAccessException)
                {
                    waiting = false;
                    await mainPage.DisplayAlertAsync("Still closing safely",
                        "Local work could not finish. Keep this window open and try Quit again; ownership and saved data were retained.", "Keep open");
                }
            }
            mainPage.RequestQuitAsync = CloseOwnedAsync;
            native.AppWindow.Closing += async (_, arguments) =>
            {
                if (completed) return;
                arguments.Cancel = true;
                await CloseOwnedAsync();
            };
        };
#endif
        return window;
    }
}
