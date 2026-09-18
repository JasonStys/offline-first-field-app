// File: MainPage.xaml.cs
// Purpose: Bind lifecycle and accessible button events to the dashboard view model.
// Symbols and line locations: see docs/code-index.md.
// Important state: viewModel is the single binding context for the page lifetime.

namespace FieldOps.App;

/// <summary>Main inspection capture and synchronization page.</summary>
public partial class MainPage : ContentPage
{
    private readonly DashboardViewModel viewModel;

    public MainPage(DashboardViewModel viewModel)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        BindingContext = viewModel;
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object? sender, EventArgs eventArgs)
    {
        await viewModel.InitializeAsync();
        SemanticScreenReader.Default.Announce(viewModel.StatusText);
    }

    private async void OnSaveClicked(object? sender, EventArgs eventArgs)
    {
        await viewModel.CreateDraftAsync();
        SemanticScreenReader.Default.Announce(viewModel.StatusText);
    }

    private async void OnSyncClicked(object? sender, EventArgs eventArgs)
    {
        await viewModel.SynchronizeAsync();
        SemanticScreenReader.Default.Announce(viewModel.StatusText);
    }
}
