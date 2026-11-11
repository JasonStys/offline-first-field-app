// File: MainPage.xaml.cs
// Purpose: Bind lifecycle and accessible button events to the dashboard view model.
// Symbols and line locations: see docs/code-index.md.
// Important state: viewModel is the single binding context for the page lifetime.

namespace FieldOps.App;

/// <summary>Main inspection capture and synchronization page.</summary>
public partial class MainPage : ContentPage
{
    private readonly DashboardViewModel viewModel;
    private readonly NativeRuntime runtime;
    private readonly NativeLaunch launch;
    private bool loaded;
    private bool closing;
    public Func<Task> RequestQuitAsync { get; set; } = () => Task.CompletedTask;

    public MainPage(DashboardViewModel viewModel, NativeRuntime runtime, NativeLaunch launch)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        this.runtime = runtime;
        this.launch = launch;
        BindingContext = viewModel;
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object? sender, EventArgs eventArgs)
    {
        if (loaded) return;
        loaded = true;
#if !WINDOWS
        StartLocalButton.IsVisible = GoOfflineButton.IsVisible = PrepareConflictButton.IsVisible = false;
#endif
        try { runtime.AcquireOwnership(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            ConnectionStatus.Text = "Workspace could not be opened safely. Close another instance or select an intact profile; no reset was attempted.";
            return;
        }
        await viewModel.InitializeAsync();
        ConnectionStatus.Text = runtime.Description;
        SemanticScreenReader.Default.Announce(viewModel.StatusText);
#if WINDOWS
        if (launch.AcceptanceRoot is not null)
            await NativeAcceptance.RunAsync(this, viewModel, runtime, launch);
#endif
    }

    public async Task CloseAsync()
    {
        closing = true;
        viewModel.BeginClose();
        MainContent.IsEnabled = false;
        ConnectionStatus.Text = "Finishing retained local work and draining the owned companion before closing...";
        await viewModel.WaitForIdleAsync();
        await runtime.DisposeAsync();
    }

    private async void OnStartLocalClicked(object? sender, EventArgs eventArgs)
    {
        if (closing) return;
        string? failure = null;
        await viewModel.PerformAsync(async () =>
        {
            try { await runtime.StartLocalAsync(); }
            catch (Exception error) when (error is IOException or InvalidOperationException or UnauthorizedAccessException)
            { failure = error.Message; throw; }
        }, "Owned local companion is ready. Sync transfers queued work to this isolated reference server.");
        ConnectionStatus.Text = failure is null ? runtime.Description : "Local companion not started. " + failure;
    }

    private async void OnGoOfflineClicked(object? sender, EventArgs eventArgs)
    {
        if (closing) return;
        await viewModel.PerformAsync(runtime.StopLocalAsync, "Offline. Existing inspections and queued commands were preserved.");
        ConnectionStatus.Text = runtime.Description;
    }

    private async void OnPrepareConflictClicked(object? sender, EventArgs eventArgs)
    {
        if (closing || viewModel.IsBusy) return;
        if (!runtime.IsLocalReady)
        {
            ConnectionStatus.Text = "Start the owned local companion before preparing its two-technician data.";
            return;
        }
        if (launch.AcceptanceRoot is null && !await DisplayAlertAsync("Prepare fictional conflict?",
            "This adds one synthetic inspection and synchronizes queued work to your owned local companion. Existing records are preserved. It does not contact an external system.", "Prepare", "Cancel")) return;
        await viewModel.PrepareConflictAsync(runtime.PrepareOtherTechnicianEditAsync);
        ShowReview();
    }

    private void OnNewClicked(object? sender, EventArgs eventArgs) => viewModel.NewInspection();
    private async void OnEditInspectionClicked(object? sender, EventArgs eventArgs)
    {
        if (closing || !viewModel.CanOperate) return;
        if (sender is BindableObject { BindingContext: FieldOps.Core.InspectionSnapshot record })
        {
            viewModel.SelectedInspection = record;
            await RecordsPane.ScrollToAsync(0, 0, animated: false);
        }
    }
    private void ShowReview() { RecordsPane.IsVisible = false; ReviewPane.IsVisible = true; }
    private void ShowRecords() { ReviewPane.IsVisible = false; RecordsPane.IsVisible = true; }
    private void OnShowReviewClicked(object? sender, EventArgs eventArgs) => ShowReview();
    private void OnShowRecordsClicked(object? sender, EventArgs eventArgs) => ShowRecords();
    private async void OnSearchClicked(object? sender, EventArgs eventArgs) => await viewModel.SearchAsync();
    private async void OnExportClicked(object? sender, EventArgs eventArgs)
    {
        if (closing || viewModel.IsBusy) return;
#if WINDOWS
        string directory;
        if (launch.AcceptanceRoot is not null) directory = Path.Combine(launch.AcceptanceRoot, launch.Reopen ? "reopen" : "first");
        else
        {
            var picker = new Windows.Storage.Pickers.FolderPicker();
            picker.FileTypeFilter.Add("*");
            var native = (Microsoft.UI.Xaml.Window)Window.Handler!.PlatformView!;
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(native));
            var folder = await picker.PickSingleFolderAsync();
            if (folder is null) return;
            directory = folder.Path;
        }
        var name = launch.AcceptanceRoot is null ? "fieldops-export-" + Guid.NewGuid().ToString("N") + ".json" : "export.json";
        await viewModel.ExportAsync(Path.Combine(directory, name));
#else
        await DisplayAlertAsync("Export availability", "The native folder export is currently implemented for Windows. The core SQLite and synchronization contracts remain cross-platform.", "Close");
#endif
    }
    private async void OnQuitClicked(object? sender, EventArgs eventArgs) => await RequestQuitAsync();
    private async void OnHelpClicked(object? sender, EventArgs eventArgs) => await DisplayAlertAsync(
        "FieldOps Offline · Jason Stys · v1.1.0",
        "Save inspections offline first. Start the owned local companion explicitly, then Sync. Select a local row to edit its accepted version. Prepare fictional two-technician data to review original/local/server fields, save your selected resolution offline, then Sync. Data and outbox remain in the existing MAUI AppData SQLite file; companion state is separate and durable. This is a trusted-OS-owner reference app, not authenticated enterprise access or a field safety authorization. Close normally before backing up the whole profile. No external ads, telemetry or remote service are added.", "Close help");

    private async void OnSaveClicked(object? sender, EventArgs eventArgs)
    {
        await viewModel.CreateDraftAsync();
        SemanticScreenReader.Default.Announce(viewModel.StatusText);
    }

    private async void OnSyncClicked(object? sender, EventArgs eventArgs)
    {
        await viewModel.SynchronizeAsync();
        if (viewModel.Conflicts.Count == 0) ShowRecords();
        else ShowReview();
        SemanticScreenReader.Default.Announce(viewModel.StatusText);
    }

    private async void OnResolveClicked(object? sender, EventArgs eventArgs)
    {
        await viewModel.ResolveSelectedConflictAsync();
        SemanticScreenReader.Default.Announce(viewModel.StatusText);
    }

    private void OnCancelReviewClicked(object? sender, EventArgs eventArgs)
    {
        viewModel.CancelConflictReview();
        SemanticScreenReader.Default.Announce(viewModel.StatusText);
    }
}
