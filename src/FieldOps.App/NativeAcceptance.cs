// File: NativeAcceptance.cs
// Purpose: Exercise the actual compiled MAUI controls, SQLite/outbox and owned real HTTP companion.
// Symbols and line locations: see docs/code-index.md.
// State: only the explicitly byte-marked fresh synthetic profile is eligible; no normal AppData is opened.

#if WINDOWS
using System.Text.Json;
using System.Reflection;
using FieldOps.Core;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace FieldOps.App;

internal static class NativeAcceptance
{
    public static async Task RunAsync(MainPage page, DashboardViewModel model, NativeRuntime runtime, NativeLaunch launch)
    {
        var output = Path.Combine(launch.AcceptanceRoot!, launch.Reopen ? "reopen" : "first");
        var steps = new List<string>();
        var passed = false;
        string? failure = null;
        object? retained = null;
        JsonElement? package = null;
        TaskCompletionSource? closeRelease = null;
        string? ownedOrigin = null;
        try
        {
            Require(!Directory.Exists(output), "Acceptance phase must be a fresh directory.");
            Directory.CreateDirectory(output);
            var identityFile = Path.Combine(AppContext.BaseDirectory, "SOURCE.json");
            using (var source = new FileStream(identityFile, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                Require(source.Length is > 0 and <= 8192, "Package identity exceeds its bound.");
                var bytes = new byte[8193];
                var count = await source.ReadAsync(bytes);
                Require(count == source.Length, "Package identity length changed.");
                using var identity = JsonDocument.Parse(bytes.AsMemory(0, count));
                package = identity.RootElement.Clone();
                Require(package.Value.GetProperty("version").GetString() == "1.1.0"
                    && package.Value.GetProperty("dotnetRuntime").GetString() == Environment.Version.ToString(),
                    "Actually loaded product/runtime differs from package provenance.");
            }
            Require(model.IsInitialized && page.Width > 0 && page.Height > 0, "Actual page failed initialization/layout.");
            var appInfo = typeof(MainPage).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
                .Where(attribute => attribute.Key.StartsWith("Microsoft.Maui.ApplicationModel.AppInfo.", StringComparison.Ordinal))
                .ToDictionary(attribute => attribute.Key, attribute => attribute.Value);
            Require(appInfo["Microsoft.Maui.ApplicationModel.AppInfo.PackageName"] == "dev.jasonstys.fieldops"
                && appInfo["Microsoft.Maui.ApplicationModel.AppInfo.PublisherName"] == "User Name",
                "Unpackaged storage identity changed from the original source; existing databases could be hidden.");
            Require(page.FindByName<Entry>("TitleEntry").TextColor.ToArgbHex() == "#12212B",
                "Actual text contrast does not match the fixed light surface.");
            if (launch.Reopen)
            {
                using var prior = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(launch.AcceptanceRoot!, "first", "acceptance.json")));
                var expected = prior.RootElement.GetProperty("retained");
                Require(model.Inspections.Count == 2 && model.Conflicts.Count == 0, "Reopen lost actual local records/resolution.");
                var rows = model.Inspections.OrderBy(item => item.Id).ToArray();
                Require(JsonSerializer.Serialize(rows) == expected.GetRawText(), "Reopen changed retained SQLite records.");
                await ClickAsync(page, model, "StartLocalButton");
                Require(runtime.IsLocalReady, "Reopen companion did not become ready.");
                await ClickAsync(page, model, "SyncButton");
                Require(model.StatusText.Contains("0 conflicts", StringComparison.Ordinal), "Reopen replay introduced a conflict.");
                Require(JsonSerializer.Serialize(model.Inspections.OrderBy(item => item.Id).ToArray()) == expected.GetRawText(),
                    "Real restarted companion replay changed accepted records.");
                retained = rows;
                steps.Add("Same two exact SQLite records and resolved versions survive restart and real HTTP replay.");
            }
            else
            {
                await ClickAsync(page, model, "SaveButton");
                Require(model.Inspections.Count == 0 && model.StatusText.Contains("required", StringComparison.Ordinal),
                    "Invalid empty title was persisted.");
                page.FindByName<Entry>("TitleEntry").Text = "Synthetic cold-chain equipment inspection";
                page.FindByName<Editor>("NotesEditor").Text = "Fictional bay7: temperature sensor calibrated before shipment.";
                await ClickAsync(page, model, "SaveButton");
                Require(model.Inspections.Count == 1 && model.Inspections[0].Version == 0, "Offline save did not retain an unsynced record.");
                await ClickAsync(page, model, "SyncButton");
                Require(model.Inspections.Count == 1 && model.StatusText.Contains("offline", StringComparison.OrdinalIgnoreCase),
                    "Offline sync lost queued work or pretended success.");
                await ClickAsync(page, model, "StartLocalButton");
                Require(runtime.IsLocalReady, "Explicit companion did not become ready.");
                await ClickAsync(page, model, "SyncButton");
                Require(model.Inspections.Single().Version == 1, "Actual initial server acceptance missing.");
                var editButton = Descendants(page.FindByName<VerticalStackLayout>("HistoryView"))
                    .OfType<Button>().Single(button => button.BindingContext is InspectionSnapshot row
                        && row.Id == model.Inspections.Single().Id);
                ((IButtonController)editButton).SendClicked();
                Require(model.SelectedInspection?.Version == 1, "Real record selection did not load its accepted base.");
                page.FindByName<Editor>("NotesEditor").Text = "Fictional bay7: follow-up vibration check passed.";
                await ClickAsync(page, model, "SaveButton");
                await ClickAsync(page, model, "SyncButton");
                Require(model.Inspections.Single().Version == 2, "Editing created a new record or lost the accepted version.");
                await CaptureAsync(page, Path.Combine(output, "native-inspections.png"));
                steps.Add("Actual empty-input refusal, offline queue/retry, owned HTTP acceptance and version-preserving row edit.");

                await ClickAsync(page, model, "PrepareConflictButton");
                Require(model.Conflicts.Count == 1 && model.HasConflictReview, "Real two-writer scenario did not produce a conflict.");
                Require(model.ConflictFields.Any(field => field.Name == "Notes" && field.SelectedIndex < 0),
                    "Divergent notes did not require a deliberate choice.");
                await ClickAsync(page, model, "ResolveButton");
                Require(model.Conflicts.Count == 1 && model.StatusText.Contains("Choose", StringComparison.Ordinal),
                    "Incomplete field review was accepted.");
                var pickers = Descendants(page).OfType<Picker>()
                    .Where(picker => picker.BindingContext is ConflictFieldChoice).ToArray();
                Require(pickers.Length == 4, "Actual conflict choice controls were not created.");
                var notes = pickers.Single(picker => ((ConflictFieldChoice)picker.BindingContext).Name == "Notes");
                await page.FindByName<ScrollView>("ReviewPane").ScrollToAsync((Element)notes.Parent.Parent,
                    ScrollToPosition.Center, animated: false);
                await CaptureAsync(page, Path.Combine(output, "native-conflict-review.png"));
                foreach (var picker in pickers)
                    if (((ConflictFieldChoice)picker.BindingContext).SelectedIndex < 0) picker.SelectedIndex = 0;
                Require(model.ConflictFields.All(field => field.SelectedIndex >= 0), "Actual picker did not bind the selected values.");
                await ClickAsync(page, model, "ResolveButton");
                await ClickAsync(page, model, "SyncButton");
                Require(model.Conflicts.Count == 0 && model.Inspections.Count == 2, "Resolved conflict was not accepted by the real server.");
                Require(model.Inspections.Single(item => item.TemplateId == "equipment-check-v1").Version == 3,
                    "Resolution did not use the latest remote base version.");
                steps.Add("Actual second writer, unresolved-field refusal, real per-field picker review and version3 resolution.");

                page.FindByName<Entry>("SearchEntry").Text = "cold-chain";
                await ClickAsync(page, model, "SearchButton");
                Require(model.Inspections.Count == 1, "Local query did not filter real SQLite data.");
                page.FindByName<Entry>("SearchEntry").Text = string.Empty;
                await ClickAsync(page, model, "SearchButton");
                await ClickAsync(page, model, "ExportButton");
                var exported = await File.ReadAllBytesAsync(Path.Combine(output, "export.json"));
                using var export = JsonDocument.Parse(exported);
                Require(export.RootElement.GetProperty("records").GetArrayLength() == 2, "Actual export omitted local rows.");
                await ClickAsync(page, model, "ExportButton");
                var preserved = await File.ReadAllBytesAsync(Path.Combine(output, "export.json"));
                Require(exported.SequenceEqual(preserved),
                    "Export overwrote an existing file.");
                Require(model.StatusText.Contains("failed safely", StringComparison.OrdinalIgnoreCase), "Existing export was not explicitly refused.");
                retained = model.Inspections.OrderBy(item => item.Id).ToArray();
                steps.Add("Actual search, JSON export and exclusive no-overwrite refusal with two retained records.");
            }
            ownedOrigin = runtime.OwnedOrigin;
            var nativeWindow = (Microsoft.UI.Xaml.Window)page.Window.Handler!.PlatformView!;
            closeRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = closeRelease;
            var pending = model.PerformAsync(async () =>
            {
                await release.Task;
                // A real local operation completes its I/O before the close handler may release ownership.
                File.WriteAllText(Path.Combine(output, "close-operation-completed.json"), "{\"completed\":true}");
            }, "Retained local operation completed.");
            ((IButtonController)page.FindByName<Button>("QuitButton")).SendClicked();
            await Task.Yield();
            Require(!pending.IsCompleted && nativeWindow.Visible, "Actual Quit did not retain the pending native operation/window.");
            var retainedLock = false;
            try
            {
                using var unexpected = new FileStream(Path.Combine(runtime.ProfileDirectory, "native-app.lock"),
                    FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException) { retainedLock = true; }
            Require(retainedLock, "Actual Quit released profile ownership before local completion.");
            steps.Add("Actual Quit retains the visible window and exclusive client lock while real local I/O is pending.");
            passed = true;
        }
        catch (Exception error) { failure = error.GetType().Name + ": " + error.Message; }
        finally
        {
            if (Directory.Exists(output))
                await File.WriteAllTextAsync(Path.Combine(output, "acceptance.json"), JsonSerializer.Serialize(new
                {
                    schema = 1,
                    product = "offline-first-field-app",
                    version = "1.1.0",
                    passed,
                    package,
                    runtime = Environment.Version.ToString(),
                    ownedOrigin,
                    processId = Environment.ProcessId,
                    normalProfileOpened = false,
                    retained,
                    steps,
                    failure,
                }));
            Environment.ExitCode = passed ? 0 : 2;
            closeRelease?.TrySetResult();
            await page.RequestQuitAsync();
        }
    }

    private static async Task ClickAsync(MainPage page, DashboardViewModel model, string name)
    {
        var button = page.FindByName<Button>(name);
        Require(button is not null && button.IsEnabled, "Actual enabled control missing: " + name);
        ((IButtonController)button!).SendClicked();
        await model.WaitForIdleAsync().WaitAsync(TimeSpan.FromSeconds(30));
        await Task.Yield();
    }

    private static IEnumerable<IVisualTreeElement> Descendants(IVisualTreeElement node)
    {
        foreach (var child in node.GetVisualChildren())
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private static async Task CaptureAsync(MainPage page, string path)
    {
        var native = (Microsoft.UI.Xaml.Window)page.Window.Handler!.PlatformView!;
        var target = new Microsoft.UI.Xaml.Media.Imaging.RenderTargetBitmap();
        await target.RenderAsync((Microsoft.UI.Xaml.FrameworkElement)native.Content);
        Require(target.PixelWidth is > 0 and <= 4096 && target.PixelHeight is > 0 and <= 4096,
            "Actual native capture dimensions invalid.");
        var buffer = await target.GetPixelsAsync();
        var bytes = new byte[buffer.Length];
        using (var reader = DataReader.FromBuffer(buffer)) reader.ReadBytes(bytes);
        using (new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
        var file = await StorageFile.GetFileFromPathAsync(path);
        using var output = await file.OpenAsync(FileAccessMode.ReadWrite);
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, output);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
            (uint)target.PixelWidth, (uint)target.PixelHeight, 96, 96, bytes);
        await encoder.FlushAsync();
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
#endif
