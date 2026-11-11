// File: NativeDashboardTests.cs
// Purpose: Verify real SQLite content search, version-preserving native editing and marked launch selection.
// Symbols and line locations: see docs/code-index.md.
// State: only newly owned temporary databases/directories; no MAUI default profile is opened.

using FieldOps.App;
using FieldOps.Core;
using System.Text.Json;

namespace FieldOps.Infrastructure.Tests;

public sealed class NativeDashboardTests
{
    [Fact]
    public async Task ContentSearch_IsLiteralAcrossTitleAndNotes_WhileOriginalPrefixContractRemains()
    {
        using var workspace = new TempWorkspace();
        var store = new LocalFieldStore(workspace.DatabasePath);
        await store.InitializeAsync();
        var first = Record("Synthetic cold-chain inspection", "Voltage100% confirmed; station_[A] calibration passed.");
        var second = Record("Synthetic pump review", "Cold-chain documentation reviewed separately.");
        await store.ApplyRemoteAsync([first, second], 2);
        Assert.Equal(2, (await store.SearchContentAsync("COLD-CHAIN", 10)).Count);
        Assert.Equal(first.Id, Assert.Single(await store.SearchContentAsync("%", 10)).Id);
        Assert.Equal(first.Id, Assert.Single(await store.SearchContentAsync("_[A]", 10)).Id);
        Assert.Empty(await store.SearchContentAsync("' OR1=1 --", 10));
        Assert.Empty(await store.SearchAsync("cold-chain", 10));
        Assert.Equal(2, (await store.SearchAsync("Synthetic", 10)).Count);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => store.SearchContentAsync(new string('x', 121), 10));
    }

    [Fact]
    public async Task Editor_RetainsActualAcceptedBaseAndOutbox_AndExportNeverOverwrites()
    {
        using var workspace = new TempWorkspace();
        var store = new LocalFieldStore(workspace.DatabasePath);
        await store.InitializeAsync();
        var record = Record("Generator room inspection", "Synthetic original check") with { Version = 4 };
        await store.ApplyRemoteAsync([record], 1);
        var model = new DashboardViewModel(store, new SyncCoordinator(store, new OfflineGateway()));
        await model.InitializeAsync();
        Assert.True(model.IsInitialized);
        model.SelectedInspection = Assert.Single(model.Inspections);
        Assert.Equal(4, model.SelectedInspection.Version);
        model.Notes = "Synthetic follow-up local check";
        await model.CreateDraftAsync();
        var queued = Assert.Single(await store.StartBatchAsync(10));
        Assert.Equal(record.Id, queued.Mutation.Record.Id);
        Assert.Equal(4, queued.Mutation.ExpectedVersion);
        Assert.Equal(model.Inspections.Single().Notes, queued.Mutation.Record.Notes);
        var databaseBefore = await File.ReadAllBytesAsync(workspace.DatabasePath);
        var exported = Path.Combine(workspace.Root, "new-export.json");
        await model.ExportAsync(exported);
        var original = await File.ReadAllBytesAsync(exported);
        await model.ExportAsync(exported);
        Assert.Contains("failed safely", model.StatusText, StringComparison.OrdinalIgnoreCase);
        var preservedExport = await File.ReadAllBytesAsync(exported);
        var databaseAfter = await File.ReadAllBytesAsync(workspace.DatabasePath);
        Assert.Equal(original, preservedExport);
        Assert.Equal(databaseBefore, databaseAfter);
    }

    [Fact]
    public async Task Export_ContainsExactlyTheDisplayedBoundedRows_AndOnlyTheAppliedFilter()
    {
        using var workspace = new TempWorkspace();
        var store = new LocalFieldStore(workspace.DatabasePath);
        await store.InitializeAsync();
        var records = Enumerable.Range(0, 105).Select(index => Record(
            "Synthetic inspection " + index, index % 2 == 0 ? "Calibration complete" : "Seal checked")).ToArray();
        await store.ApplyRemoteAsync(records, 105);
        var model = new DashboardViewModel(store, new SyncCoordinator(store, new OfflineGateway()));
        await model.InitializeAsync();
        Assert.Equal(100, model.Inspections.Count);
        var shown = model.Inspections.Select(record => record.Id).ToArray();
        model.SearchText = "not yet applied";
        var path = Path.Combine(workspace.Root, "displayed.json");
        await model.ExportAsync(path);
        using var first = JsonDocument.Parse(await File.ReadAllBytesAsync(path));
        Assert.Equal(100, first.RootElement.GetProperty("maximumRecords").GetInt32());
        Assert.Equal("", first.RootElement.GetProperty("search").GetString());
        Assert.Equal(shown, first.RootElement.GetProperty("records").EnumerateArray()
            .Select(record => record.GetProperty("Id").GetGuid()).ToArray());
        model.SearchText = "Calibration";
        await model.SearchAsync();
        Assert.Equal(53, model.Inspections.Count);
        var filtered = Path.Combine(workspace.Root, "filtered.json");
        await model.ExportAsync(filtered);
        using var second = JsonDocument.Parse(await File.ReadAllBytesAsync(filtered));
        Assert.Equal("Calibration", second.RootElement.GetProperty("search").GetString());
        Assert.Equal(model.Inspections.Select(record => record.Id),
            second.RootElement.GetProperty("records").EnumerateArray()
                .Select(record => record.GetProperty("Id").GetGuid()));
        Assert.Equal(105, (await store.SearchContentAsync(null, 200)).Count);
    }

    [Fact]
    public void NativeAcceptance_RequiresExactBytesAndExplicitFreshOrReopenProfile()
    {
        using var workspace = new TempWorkspace();
        var marker = Path.Combine(workspace.Root, "acceptance.marker");
        File.WriteAllBytes(marker, "fieldops-owned-native-acceptance-v1\r\n"u8.ToArray());
        var args = new[] { "application.exe", "--native-acceptance-root", workspace.Root };
        Assert.Throws<InvalidDataException>(() => NativeLaunch.Read(args));
        File.WriteAllBytes(marker, "fieldops-owned-native-acceptance-v1\n"u8.ToArray());
        Assert.Equal(workspace.Root, NativeLaunch.Read(args).AcceptanceRoot);
        Directory.CreateDirectory(Path.Combine(workspace.Root, "profile"));
        Assert.Throws<InvalidDataException>(() => NativeLaunch.Read(args));
        Assert.True(NativeLaunch.Read([.. args, "--acceptance-reopen"]).Reopen);
        Assert.Throws<ArgumentException>(() => NativeLaunch.Read(["application.exe", "--acceptance-reopen"]));
        Assert.Throws<ArgumentException>(() => NativeLaunch.Read(["application.exe", "--unknown"]));
        Assert.Null(NativeLaunch.Read(["application.exe"]).AcceptanceRoot);
    }

    private static InspectionSnapshot Record(string title, string notes) => InspectionSnapshot.Create(
        Guid.NewGuid(), "equipment-v1", title, notes, InspectionStatus.Draft, 1, DateTimeOffset.UnixEpoch).Value!;

    private sealed class OfflineGateway : IRemoteSyncGateway
    {
        public Task<IReadOnlyList<PushItemResult>> PushAsync(PushRequest request, CancellationToken cancellationToken = default)
            => throw new HttpRequestException("Isolated offline test transport.");
        public Task<PullResponse> PullAsync(long afterCursor, int maximumCount, CancellationToken cancellationToken = default)
            => throw new HttpRequestException("Isolated offline test transport.");
    }
}
