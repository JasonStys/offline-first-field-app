// File: NativeRuntime.cs
// Purpose: Select an explicit owned companion for the actual client without changing its SQLite store.
// Symbols and line locations: see docs/code-index.md.
// State: endpoint ownership is volatile; normal durable data stays at the existing MAUI AppData path.

using FieldOps.Core;
using FieldOps.Infrastructure;
using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
#if WINDOWS
using FieldOps.Api;
#endif

namespace FieldOps.App;

/// <summary>Same original sync protocol; offline is the initial Windows mode, never guessed port5080.</summary>
public sealed class NativeRuntime : IRemoteSyncGateway, IAsyncDisposable
{
    private readonly SemaphoreSlim operations = new(1, 1);
    private FileStream? ownership;
#if WINDOWS
    private NativeCompanion? companion;
#endif
    public string ProfileDirectory { get; }
    public bool IsLocalReady { get; private set; }
    public string Description => IsLocalReady
        ? "Owned local companion ready. Trusted OS-owner reference; no external service."
        : "Offline. Your SQLite records and queued work remain available.";

    public NativeRuntime(string directory)
    {
        ProfileDirectory = Path.GetFullPath(directory);
    }

    /// <summary>Retain this client's real SQLite/outbox ownership before any schema or sync operation.</summary>
    public void AcquireOwnership()
    {
        if (ownership is not null) return;
        var install = Path.TrimEndingDirectorySeparator(Path.GetFullPath(AppContext.BaseDirectory));
        if (string.Equals(ProfileDirectory, install, StringComparison.OrdinalIgnoreCase)
            || ProfileDirectory.StartsWith(install + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Keep durable workspace data outside the installed application.");
        for (var part = new DirectoryInfo(ProfileDirectory); part is not null; part = part.Parent)
        {
            var attributes = AttributesIfPresent(part.FullName);
            if (attributes is not null && ((attributes & FileAttributes.ReparsePoint) != 0
                || (attributes & FileAttributes.Directory) == 0))
                throw new InvalidDataException("Native profile ancestors must be real directories.");
        }
        Directory.CreateDirectory(ProfileDirectory);
        foreach (var name in new[] { "native-app.lock", "fieldops.db", "fieldops.db-wal", "fieldops.db-shm", "fieldops.db-journal" })
        {
            var path = Path.Combine(ProfileDirectory, name);
            var attributes = AttributesIfPresent(path);
            if (attributes is not null && (attributes & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0)
                throw new InvalidDataException("Native SQLite/ownership state must be ordinary files.");
            if (attributes is not null && OperatingSystem.IsWindows()) RequireSingleLinkWindows(path);
        }
        ownership = new FileStream(Path.Combine(ProfileDirectory, "native-app.lock"), FileMode.OpenOrCreate,
            FileAccess.ReadWrite, FileShare.None);
    }

    /// <summary>Inspect aliases even when their targets are missing; absence alone is admissible.</summary>
    private static FileAttributes? AttributesIfPresent(string path)
    {
        try { return File.GetAttributes(path); }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }

    /// <summary>Read Windows metadata without following final reparse points or opening file data for writes.</summary>
    [SupportedOSPlatform("windows")]
    private static void RequireSingleLinkWindows(string path)
    {
        const uint readAttributes = 0x80;
        const uint shareReadWriteDelete = 0x7;
        const uint openExisting = 3;
        const uint openReparsePoint = 0x00200000;
        using var handle = OpenMetadata(path, readAttributes, shareReadWriteDelete, IntPtr.Zero,
            openExisting, openReparsePoint, IntPtr.Zero);
        if (handle.IsInvalid || !ReadFileInformation(handle, out var information)
            || information.NumberOfLinks != 1
            || (information.Attributes & (uint)(FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0)
            throw new InvalidDataException("Native mutable state must be a single-link ordinary file.");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileInformation
    {
        public uint Attributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME AccessTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME WriteTime;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }

    // Fixed OS exports only. SafeFileHandle owns closure even on refusal; no DLL path is user-controlled.
    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [SupportedOSPlatform("windows")]
    private static extern SafeFileHandle OpenMetadata(string path, uint access, uint sharing,
        IntPtr securityAttributes, uint creation, uint flags, IntPtr template);

    [DllImport("kernel32.dll", EntryPoint = "GetFileInformationByHandle", ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [SupportedOSPlatform("windows")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReadFileInformation(SafeFileHandle handle, out FileInformation information);

    /// <summary>Start only a newly owned real reference API, retaining its checkpoint across restarts.</summary>
    public async Task StartLocalAsync()
    {
        await operations.WaitAsync();
        try
        {
            if (IsLocalReady) return;
            if (ownership is null) throw new InvalidOperationException("Open the local workspace first.");
#if WINDOWS
            var local = new LocalFieldStore(Path.Combine(ProfileDirectory, "fieldops.db"));
            var authority = new NativeAuthority(ProfileDirectory);
            var expected = await authority.ReadExpectedAsync(local);
            var candidate = await NativeCompanion.StartAsync(Path.Combine(ProfileDirectory, "native-companion"));
            try { await authority.BindAsync(local, expected, candidate.InstanceId, candidate.CurrentCursor); }
            catch { await candidate.DisposeAsync(); throw; }
            companion = candidate;
            IsLocalReady = true;
#else
            throw new NotSupportedException("The bundled local companion is available in Windows builds.");
#endif
        }
        finally { operations.Release(); }
    }

    public async Task<IReadOnlyList<PushItemResult>> PushAsync(PushRequest request, CancellationToken cancellationToken = default)
    {
        await operations.WaitAsync(cancellationToken);
        try
        {
            using var client = CreateClient();
            return await new HttpSyncGateway(client).PushAsync(request, cancellationToken);
        }
        finally { operations.Release(); }
    }

    public async Task<PullResponse> PullAsync(long afterCursor, int maximumCount, CancellationToken cancellationToken = default)
    {
        await operations.WaitAsync(cancellationToken);
        try
        {
            using var client = CreateClient();
            return await new HttpSyncGateway(client).PullAsync(afterCursor, maximumCount, cancellationToken);
        }
        finally { operations.Release(); }
    }

    private HttpClient CreateClient()
    {
#if WINDOWS
        return companion?.CreateClient() ?? throw new HttpRequestException("Explicitly start the local companion first.");
#else
        return new HttpClient { BaseAddress = new Uri("https://10.0.2.2:7080"), Timeout = TimeSpan.FromSeconds(5) };
#endif
    }

    public string? OwnedOrigin
    {
        get
        {
#if WINDOWS
            return companion?.Address.AbsoluteUri;
#else
            return null;
#endif
        }
    }

    /// <summary>Prepared second-writer data uses the same real owned HTTP version-checking contract.</summary>
    public async Task PrepareOtherTechnicianEditAsync(InspectionSnapshot record)
    {
        if (!IsLocalReady || record.Version < 1)
            throw new InvalidOperationException("Start the local companion and sync the record first.");
        var edited = record with { Notes = "Synthetic second technician: replace filter before release." };
        var result = await PushAsync(new PushRequest([
            new InspectionMutation(Guid.NewGuid(), "prepared-second-technician", record.Version, edited)]));
        if (result.Count != 1 || !result[0].Applied)
            throw new InvalidOperationException("Prepared second-writer action was not accepted; inspect the latest version.");
    }

    /// <summary>Wait for every retained gateway operation and real companion shutdown; never kill it.</summary>
    public async Task StopLocalAsync()
    {
        await operations.WaitAsync();
        try { await StopOwnedAsync(); }
        finally { operations.Release(); }
    }

    private async Task StopOwnedAsync()
    {
#if WINDOWS
        if (companion is not null) await companion.DisposeAsync();
        companion = null;
#else
        await Task.CompletedTask;
#endif
        IsLocalReady = false;
    }

    /// <summary>Release the client profile only after all owned service work actually completes.</summary>
    public async ValueTask DisposeAsync()
    {
        await operations.WaitAsync();
        try
        {
            await StopOwnedAsync();
            if (ownership is not null) await ownership.DisposeAsync();
            ownership = null;
        }
        finally { operations.Release(); }
    }
}
