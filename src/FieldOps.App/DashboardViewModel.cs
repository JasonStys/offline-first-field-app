// File: DashboardViewModel.cs
// Purpose: Present offline draft creation, search history, sync status, and retry-safe failures.
// Symbols and line locations: see docs/code-index.md.
// Important state: inspections is observable UI state; IsBusy prevents overlapping operations.

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using FieldOps.Core;
using FieldOps.Infrastructure;

namespace FieldOps.App;

/// <summary>Platform-neutral dashboard behavior used by the MAUI page.</summary>
public sealed class DashboardViewModel(LocalFieldStore store, SyncCoordinator coordinator)
    : INotifyPropertyChanged
{
    private string title = string.Empty;
    private string notes = string.Empty;
    private string statusText = "Ready for offline work.";
    private bool isBusy;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<InspectionSnapshot> Inspections { get; } = [];

    public string Title
    {
        get => title;
        set => SetField(ref title, value);
    }

    public string Notes
    {
        get => notes;
        set => SetField(ref notes, value);
    }

    public string StatusText
    {
        get => statusText;
        private set => SetField(ref statusText, value);
    }

    public bool IsBusy
    {
        get => isBusy;
        private set => SetField(ref isBusy, value);
    }

    /// <summary>Initialize the local schema and load recent records.</summary>
    public async Task InitializeAsync()
    {
        await RunAsync(
            async () =>
            {
                await store.InitializeAsync();
                await ReloadAsync();
                StatusText = "Local workspace is ready.";
            });
    }

    /// <summary>Validate and transactionally queue one synthetic inspection while offline.</summary>
    public async Task CreateDraftAsync()
    {
        await RunAsync(
            async () =>
            {
                var validation = InspectionSnapshot.Create(
                    Guid.NewGuid(),
                    "field-check-v1",
                    Title,
                    Notes,
                    InspectionStatus.Draft,
                    0,
                    DateTimeOffset.UtcNow);
                if (!validation.IsValid)
                {
                    StatusText = string.Join(" ", validation.Errors);
                    return;
                }

                await store.SaveDraftAsync(validation.Value!, "field-app-device");
                Title = string.Empty;
                Notes = string.Empty;
                await ReloadAsync();
                StatusText = "Draft saved locally and queued for synchronization.";
            });
    }

    /// <summary>Attempt a bounded sync and keep commands pending if the endpoint is unavailable.</summary>
    public async Task SynchronizeAsync()
    {
        await RunAsync(
            async () =>
            {
                try
                {
                    var report = await coordinator.SynchronizeAsync();
                    await ReloadAsync();
                    StatusText =
                        $"Sync complete: {report.Applied} applied, {report.Conflicted} conflicts, "
                        + $"{report.Pulled} pulled.";
                }
                catch (HttpRequestException)
                {
                    StatusText = "Still offline. Queued work remains available for retry.";
                }
                catch (TaskCanceledException)
                {
                    StatusText = "Sync timed out. Queued work remains available for retry.";
                }
            });
    }

    private async Task ReloadAsync()
    {
        var records = await store.SearchAsync(null, 100);
        Inspections.Clear();
        foreach (var record in records)
        {
            Inspections.Add(record);
        }
    }

    private async Task RunAsync(Func<Task> operation)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await operation();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            StatusText = $"Operation failed safely: {exception.GetType().Name}.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
