// File: App.xaml.cs
// Purpose: Create the single MAUI window from the dependency-injected field dashboard.
// Symbols and line locations: see docs/code-index.md.
// Important state: mainPage is resolved once and owned by the application window.

namespace FieldOps.App;

/// <summary>Application root for Android and Windows targets.</summary>
public partial class App : Application
{
    private readonly MainPage mainPage;

    public App(MainPage mainPage)
    {
        InitializeComponent();
        this.mainPage = mainPage;
    }

    protected override Window CreateWindow(IActivationState? activationState) => new(mainPage);
}
