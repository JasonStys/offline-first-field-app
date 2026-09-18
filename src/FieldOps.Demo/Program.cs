// File: Program.cs
// Purpose: Demonstrate offline sync/conflict behavior or run bounded local performance checks.
// Symbols and line locations: see docs/code-index.md.
// Important variables: workspace is disposable; benchmark limits are conservative CI guardrails.

using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using FieldOps.Core;
using FieldOps.Demo;
using FieldOps.Infrastructure;

return args.Contains("--benchmark", StringComparer.Ordinal)
    ? await RunBenchmarkAsync()
    : await RunScenarioAsync();

static async Task<int> RunScenarioAsync()
{
    var workspace = CreateWorkspace();
    try
    {
        var store = new LocalFieldStore(Path.Combine(workspace, "fieldops.db"));
        await store.InitializeAsync();
        var offlineRecord = InspectionSnapshot.Create(
            Guid.NewGuid(),
            "safety-walk-v1",
            "North loading bay",
            "Guard rail fixture generated for the demo.",
            InspectionStatus.ReadyForReview,
            0,
            DateTimeOffset.UnixEpoch).Value!;
        await store.SaveDraftAsync(offlineRecord, "demo-device");

        var coordinator = new SyncCoordinator(store, new DeterministicGateway());
        var report = await coordinator.SynchronizeAsync();
        var synchronized = (await store.SearchAsync("North", 10)).Single();
        var baseline = synchronized;
        var local = baseline with { Notes = "Local reviewer changed this note." };
        var remote = baseline with { Notes = "Remote reviewer changed this note.", Version = 2 };
        var conflict = ConflictMergePolicy.Merge(baseline, local, remote);

        WriteJson(
            new
            {
                mode = "offline-create-then-sync",
                report,
                synchronized.Id,
                synchronized.Version,
                conflict.HasConflict,
                conflict.ConflictingFields,
            });
        return conflict.HasConflict && report.Applied == 1 ? 0 : 1;
    }
    finally
    {
        DeleteWorkspace(workspace);
    }
}

static async Task<int> RunBenchmarkAsync()
{
    const int operationCount = 100;
    const double maximumP95Milliseconds = 250;
    const double maximumSearchMilliseconds = 500;
    const long maximumAllocatedBytes = 64 * 1024 * 1024;
    var workspace = CreateWorkspace();
    try
    {
        var store = new LocalFieldStore(Path.Combine(workspace, "fieldops.db"));
        await store.InitializeAsync();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        var durations = new double[operationCount];
        for (var index = 0; index < operationCount; index++)
        {
            var record = InspectionSnapshot.Create(
                Guid.NewGuid(),
                "benchmark-v1",
                "Inspection " + index.ToString(CultureInfo.InvariantCulture),
                "Generated benchmark fixture.",
                InspectionStatus.Draft,
                0,
                DateTimeOffset.UnixEpoch.AddSeconds(index)).Value!;
            var stopwatch = Stopwatch.StartNew();
            await store.SaveDraftAsync(record, "benchmark-device");
            stopwatch.Stop();
            durations[index] = stopwatch.Elapsed.TotalMilliseconds;
        }

        var searchTimer = Stopwatch.StartNew();
        var records = await store.SearchAsync("Inspection", 200);
        searchTimer.Stop();
        var allocatedBytes = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;
        var p95 = durations.Order().ElementAt((int)Math.Ceiling(operationCount * 0.95) - 1);
        var passed = p95 <= maximumP95Milliseconds
            && searchTimer.Elapsed.TotalMilliseconds <= maximumSearchMilliseconds
            && allocatedBytes <= maximumAllocatedBytes
            && records.Count == operationCount;
        WriteJson(
            new
            {
                mode = "local-sqlite-budget",
                operationCount,
                queueWriteP95Milliseconds = Math.Round(p95, 3),
                searchMilliseconds = Math.Round(searchTimer.Elapsed.TotalMilliseconds, 3),
                allocatedBytes,
                budgets = new
                {
                    maximumP95Milliseconds,
                    maximumSearchMilliseconds,
                    maximumAllocatedBytes,
                },
                passed,
            });
        return passed ? 0 : 1;
    }
    finally
    {
        DeleteWorkspace(workspace);
    }
}

static string CreateWorkspace()
{
    var path = Path.Combine(Path.GetTempPath(), "fieldops-demo-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(path);
    return path;
}

static void DeleteWorkspace(string path)
{
    if (path.StartsWith(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase))
    {
        Directory.Delete(path, recursive: true);
    }
}

static void WriteJson<T>(T value) =>
    Console.WriteLine(JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
