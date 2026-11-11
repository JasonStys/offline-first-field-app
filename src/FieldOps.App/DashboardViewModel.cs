// File: DashboardViewModel.cs
// Purpose: Present offline drafts, synchronization and explicit base/local/remote conflict review.
// Symbols and line locations: see docs/code-index.md.
// Important state: inspections is observable UI state; IsBusy prevents overlapping operations.

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
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
    private ConflictReview? selectedConflict;
    private InspectionSnapshot? selectedInspection;
    private string templateId = "field-check-v1";
    private string searchText = string.Empty;
    private string displayedSearch = string.Empty;
    private InspectionStatus inspectionStatus = InspectionStatus.Draft;
    private Task currentOperation = Task.CompletedTask;
    private bool isInitialized;
    private bool isClosing;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<InspectionSnapshot> Inspections { get; } = [];
    public ObservableCollection<ConflictReview> Conflicts { get; } = [];
    public ObservableCollection<ConflictFieldChoice> ConflictFields { get; } = [];
    public IReadOnlyList<InspectionStatus> StatusOptions { get; } = Enum.GetValues<InspectionStatus>();
    public bool HasConflictReview => selectedConflict is not null;
    public bool HasInspections => Inspections.Count > 0;
    public bool IsInitialized
    {
        get => isInitialized;
        private set { if (SetField(ref isInitialized, value)) PropertyChanged?.Invoke(this, new(nameof(CanOperate))); }
    }
    public bool CanOperate => IsInitialized && !IsBusy && !isClosing;

    /// <summary>Prevent new commands while retaining the actual operation already in progress.</summary>
    public void BeginClose()
    {
        isClosing = true;
        PropertyChanged?.Invoke(this, new(nameof(CanOperate)));
    }

    /// <summary>Editing retains the actual accepted version; a row is never silently recreated.</summary>
    public InspectionSnapshot? SelectedInspection
    {
        get => selectedInspection;
        set
        {
            if (IsBusy && value is not null) return;
            if (!SetField(ref selectedInspection, value)) return;
            if (value is not null)
            {
                Title = value.Title;
                Notes = value.Notes;
                TemplateId = value.TemplateId;
                InspectionStatus = value.Status;
            }
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EditSummary)));
        }
    }

    public string EditSummary => SelectedInspection is null ? "New inspection (offline draft)"
        : $"Editing {SelectedInspection.Id:D}; accepted version {SelectedInspection.Version}.";
    public string TemplateId { get => templateId; set => SetField(ref templateId, value); }
    public string SearchText { get => searchText; set => SetField(ref searchText, value); }
    public InspectionStatus InspectionStatus { get => inspectionStatus; set => SetField(ref inspectionStatus, value); }

    /// <summary>Clear only the editor; persisted inspections and queued commands remain untouched.</summary>
    public void NewInspection()
    {
        if (IsBusy) return;
        ClearEditor();
        StatusText = "New editor ready. Existing inspections and outbox were not changed.";
    }

    private void ClearEditor()
    {
        SelectedInspection = null;
        Title = Notes = string.Empty;
        TemplateId = "field-check-v1";
        InspectionStatus = InspectionStatus.Draft;
    }

    public ConflictReview? SelectedConflict
    {
        get => selectedConflict;
        set
        {
            if (value == selectedConflict) return;
            SetField(ref selectedConflict, value);
            ConflictFields.Clear();
            if (value is not null)
            {
                ConflictFields.Add(new("TemplateId", value.Baseline?.TemplateId, value.Local.TemplateId, value.Remote.TemplateId));
                ConflictFields.Add(new("Title", value.Baseline?.Title, value.Local.Title, value.Remote.Title));
                ConflictFields.Add(new("Notes", value.Baseline?.Notes, value.Local.Notes, value.Remote.Notes));
                ConflictFields.Add(new("Status", value.Baseline?.Status.ToString(), value.Local.Status.ToString(), value.Remote.Status.ToString()));
                StatusText = $"Review all fields against remote version {value.Remote.Version}. Divergent fields need your choice.";
            }

            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasConflictReview)));
        }
    }

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
        private set { if (SetField(ref isBusy, value)) PropertyChanged?.Invoke(this, new(nameof(CanOperate))); }
    }

    /// <summary>Initialize the local schema and load recent records.</summary>
    public async Task InitializeAsync()
    {
        await RunAsync(
            async () =>
            {
                await store.InitializeAsync();
                await ReloadAsync();
                IsInitialized = true;
                StatusText = "Local workspace is ready.";
            });
    }

    /// <summary>Validate and queue a new or explicitly selected inspection without losing its base.</summary>
    public async Task CreateDraftAsync()
    {
        await RunAsync(
            async () =>
            {
                var validation = InspectionSnapshot.Create(
                    SelectedInspection?.Id ?? Guid.NewGuid(),
                    TemplateId,
                    Title,
                    Notes,
                    InspectionStatus,
                    SelectedInspection?.Version ?? 0,
                    DateTimeOffset.UtcNow);
                if (!validation.IsValid)
                {
                    StatusText = string.Join(" ", validation.Errors);
                    return;
                }

                await store.SaveDraftAsync(validation.Value!, "field-app-device");
                ClearEditor();
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

    /// <summary>Save chosen values locally as a fresh version-checked command; no network is required.</summary>
    public async Task ResolveSelectedConflictAsync()
    {
        await RunAsync(async () =>
        {
            var review = SelectedConflict;
            if (review is null)
            {
                StatusText = "Select a conflict to review first.";
                return;
            }
            var missing = ConflictFields.FirstOrDefault(field => field.SelectedIndex < 0);
            if (missing is not null)
            {
                StatusText = $"Choose the local or remote value for {missing.Name}.";
                return;
            }
            var choices = ConflictFields.ToDictionary(field => field.Name, field => field.GetSelection(), StringComparer.Ordinal);
            try
            {
                await store.ResolveConflictAsync(review, choices, "field-app-device");
            }
            catch (InvalidOperationException)
            {
                await ReloadAsync();
                StatusText = "Conflict evidence changed or other queued work prevents resolution. Select and review the latest values.";
                return;
            }
            catch (ArgumentException)
            {
                StatusText = "Resolution values are invalid. Review every field before saving.";
                return;
            }
            SelectedConflict = null;
            await ReloadAsync();
            StatusText = "Resolution saved offline and queued. Sync now when connected; newer remote edits will require another review.";
        });
    }

    /// <summary>Leave all persisted evidence and queued commands unchanged.</summary>
    public void CancelConflictReview()
    {
        SelectedConflict = null;
        StatusText = "Review canceled. Both versions remain saved for later review.";
    }

    /// <summary>Filter actual local rows; the visible list remains explicitly limited to100.</summary>
    public Task SearchAsync() => RunAsync(async () =>
    {
        if (SearchText.Length > 120) { StatusText = "Search must not exceed120characters."; return; }
        await ReloadAsync();
        StatusText = $"Showing {Inspections.Count} matching local inspections (maximum100).";
    });

    /// <summary>Run connection/setup actions under the same retained UI operation boundary.</summary>
    public Task PerformAsync(Func<Task> action, string success) => RunAsync(async () =>
    {
        await action();
        StatusText = success;
    });

    /// <summary>Prepare fictional two-technician data through the original real sync pipeline.</summary>
    public Task PrepareConflictAsync(Func<InspectionSnapshot, Task> otherWriter) => RunAsync(async () =>
    {
        var record = InspectionSnapshot.Create(Guid.NewGuid(), "equipment-check-v1",
            "Synthetic pump maintenance inspection", "Initial pressure check passed.",
            InspectionStatus.ReadyForReview, 0, DateTimeOffset.UtcNow).Value!;
        await store.SaveDraftAsync(record, "field-app-device");
        await coordinator.SynchronizeAsync();
        var accepted = (await store.SearchAsync(record.Title, 100)).Single(item => item.Id == record.Id);
        if (accepted.Version != 1) throw new InvalidOperationException("Prepared initial sync did not complete.");
        await otherWriter(accepted);
        await store.SaveDraftAsync(accepted with { Notes = "Synthetic local technician: pump vibration needs review." }, "field-app-device");
        await coordinator.SynchronizeAsync();
        SearchText = string.Empty;
        await ReloadAsync();
        SelectedConflict = Conflicts.Single(item => item.Local.Id == record.Id);
        StatusText = "Prepared actual two-writer conflict. Review base/local/server; choose every divergent field, save offline, then sync.";
    });

    /// <summary>Expose actual retained completion to native close; no timeout drops the local lock.</summary>
    public Task WaitForIdleAsync() => currentOperation;

    /// <summary>Export a bounded actual local query to a new user-selected file, never overwrite.</summary>
    public Task ExportAsync(string destination) => RunAsync(async () =>
    {
        // Export the already displayed snapshot, including its applied filter, not hidden extra rows.
        var records = Inspections.ToArray();
        var payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            schema = 1,
            product = "offline-first-field-app",
            version = "1.1.0",
            search = displayedSearch,
            maximumRecords = 100,
            records,
            scope = "Current local SQLite query; not a full backup of outbox/conflict/server state.",
        }, new JsonSerializerOptions { WriteIndented = true });
        if (payload.Length > 2 * 1024 * 1024) throw new InvalidDataException("Export byte budget exceeded.");
        await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await output.WriteAsync(payload);
        output.Flush(flushToDisk: true);
        StatusText = $"Exported {records.Length} actual local rows to a new JSON file; outbox and server state were unchanged.";
    });

    private async Task ReloadAsync()
    {
        var query = SearchText;
        var records = await store.SearchContentAsync(query, 100);
        displayedSearch = query;
        SelectedInspection = null;
        Inspections.Clear();
        foreach (var record in records)
        {
            Inspections.Add(record);
        }
        PropertyChanged?.Invoke(this, new(nameof(HasInspections)));

        SelectedConflict = null;
        var conflicts = await store.GetConflictsAsync(100);
        Conflicts.Clear();
        foreach (var conflict in conflicts) Conflicts.Add(conflict);
    }

    private Task RunAsync(Func<Task> operation)
    {
        if (IsBusy || isClosing)
        {
            return Task.CompletedTask;
        }

        IsBusy = true;
        currentOperation = ExecuteAsync(operation);
        return currentOperation;
    }

    private async Task ExecuteAsync(Func<Task> operation)
    {
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

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}
