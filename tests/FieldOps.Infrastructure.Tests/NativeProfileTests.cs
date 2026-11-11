// File: NativeProfileTests.cs
// Purpose: Refuse aliases/install-folder state before original SQLite initialization can touch it.
// Symbols and line locations: see docs/code-index.md.
// State: real owned temporary files and junctions only; no normal AppData or installed app is opened.

using System.Diagnostics;
using FieldOps.App;
using FieldOps.Infrastructure;

namespace FieldOps.Infrastructure.Tests;

public sealed class NativeProfileTests
{
    [Theory]
    [InlineData("native-app.lock")]
    [InlineData("fieldops.db")]
    [InlineData("fieldops.db-wal")]
    [InlineData("fieldops.db-shm")]
    [InlineData("fieldops.db-journal")]
    public async Task DirectoryAtFixedStateFile_IsRefusedWithoutChangingIt(string name)
    {
        using var workspace = new TempWorkspace();
        var profile = Path.Combine(workspace.Root, "profile");
        var occupied = Path.Combine(profile, name);
        Directory.CreateDirectory(occupied);
        var retained = Path.Combine(occupied, "keep.txt");
        await File.WriteAllTextAsync(retained, "Retain this synthetic unrelated content");
        await using var runtime = new NativeRuntime(profile);
        Assert.Throws<InvalidDataException>(runtime.AcquireOwnership);
        Assert.Equal("Retain this synthetic unrelated content", await File.ReadAllTextAsync(retained));
        Assert.Single(Directory.GetFileSystemEntries(profile));
    }

    [Fact]
    public async Task OrdinaryExistingSqliteSidecars_ArePreservedBeforeAnySchemaWork()
    {
        using var workspace = new TempWorkspace();
        var profile = Path.Combine(workspace.Root, "profile");
        Directory.CreateDirectory(profile);
        string[] names = ["fieldops.db", "fieldops.db-wal", "fieldops.db-shm", "fieldops.db-journal"];
        foreach (var name in names) await File.WriteAllBytesAsync(Path.Combine(profile, name), [1, 2, 3, 4]);
        await using (var runtime = new NativeRuntime(profile)) runtime.AcquireOwnership();
        foreach (var name in names) Assert.Equal(new byte[] { 1, 2, 3, 4 }, await File.ReadAllBytesAsync(Path.Combine(profile, name)));
    }

    [Fact]
    public async Task InstallationFolderProfile_IsRefusedBeforeCreation()
    {
        var profile = Path.Combine(AppContext.BaseDirectory, "native-refused-" + Guid.NewGuid().ToString("N"));
        Assert.False(Directory.Exists(profile));
        await using var runtime = new NativeRuntime(profile);
        Assert.Throws<InvalidDataException>(runtime.AcquireOwnership);
        Assert.False(Directory.Exists(profile));
    }

    [WindowsTheory]
    [InlineData("native-app.lock")]
    [InlineData("fieldops.db")]
    [InlineData("fieldops.db-wal")]
    [InlineData("fieldops.db-shm")]
    [InlineData("fieldops.db-journal")]
    public async Task ActualNtfsHardlink_IsRefusedBeforeSqliteOrOwnershipCanMutateItsTarget(string name)
    {
        using var workspace = new TempWorkspace();
        var profile = Path.Combine(workspace.Root, "profile");
        Directory.CreateDirectory(profile);
        var database = Path.Combine(profile, "fieldops.db");
        await new LocalFieldStore(database).InitializeAsync();
        var originalDatabase = await File.ReadAllBytesAsync(database);
        var target = Path.Combine(workspace.Root, "external-sentinel.bin");
        var alias = Path.Combine(profile, name);
        if (name == "fieldops.db")
        {
            await File.WriteAllBytesAsync(target, originalDatabase);
            File.Delete(database);
        }
        else await File.WriteAllBytesAsync(target, Enumerable.Repeat((byte)55, 55).ToArray());
        var originalTarget = await File.ReadAllBytesAsync(target);
        RunAliasCommand("/H", alias, target);
        try
        {
            // The original attribute-only predicate accepts an NTFS hardlink as an ordinary file.
            Assert.Equal(0, (int)(File.GetAttributes(alias) & (FileAttributes.ReparsePoint | FileAttributes.Directory)));
            await using var runtime = new NativeRuntime(profile);
            Assert.Throws<InvalidDataException>(runtime.AcquireOwnership);
            Assert.Equal(originalTarget, await File.ReadAllBytesAsync(target));
            Assert.Equal(originalDatabase, await File.ReadAllBytesAsync(database));
            if (name != "native-app.lock") Assert.False(File.Exists(Path.Combine(profile, "native-app.lock")));
        }
        finally { File.Delete(alias); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ActualDirectoryAlias_AncestorOrSqliteSidecar_IsRefusedBeforeTargetMutation(bool sidecar)
    {
        using var workspace = new TempWorkspace();
        var target = Path.Combine(workspace.Root, "retained-target");
        Directory.CreateDirectory(target);
        var retained = Path.Combine(target, "keep.txt");
        await File.WriteAllTextAsync(retained, "Retained synthetic junction target");
        var profile = Path.Combine(workspace.Root, "profile");
        Directory.CreateDirectory(profile);
        var alias = sidecar ? Path.Combine(profile, "fieldops.db-wal") : Path.Combine(workspace.Root, "alias");
        CreateDirectoryAlias(alias, target);
        try
        {
            await using var runtime = new NativeRuntime(sidecar ? profile : Path.Combine(alias, "new-state"));
            Assert.Throws<InvalidDataException>(runtime.AcquireOwnership);
            Assert.Equal("Retained synthetic junction target", await File.ReadAllTextAsync(retained));
            Assert.False(Directory.Exists(Path.Combine(target, "new-state")));
            Assert.Single(Directory.GetFileSystemEntries(target));
            Assert.False(File.Exists(Path.Combine(profile, "native-app.lock")));
        }
        finally { Directory.Delete(alias); }
    }

    /// <summary>Create a real no-admin Windows junction or POSIX link inside the owned test root.</summary>
    private static void CreateDirectoryAlias(string alias, string target)
    {
        if (!OperatingSystem.IsWindows())
        {
            Directory.CreateSymbolicLink(alias, target);
            return;
        }
        RunAliasCommand("/J", alias, target);
        Assert.True((File.GetAttributes(alias) & FileAttributes.ReparsePoint) != 0);
    }

    /// <summary>Use only a fixed Windows link builtin and generated paths inside this test's owned root.</summary>
    private static void RunAliasCommand(string flag, string alias, string target)
    {
        if (flag is not ("/H" or "/J")) throw new ArgumentOutOfRangeException(nameof(flag));
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe"))
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in new[] { "/d", "/c", "mklink", flag, alias, target }) start.ArgumentList.Add(argument);
        using var child = Process.Start(start) ?? throw new InvalidOperationException("Owned junction creation did not start.");
        if (!child.WaitForExit(5000)) throw new InvalidOperationException("Owned junction fixture exceeded its bound.");
        Assert.Equal(0, child.ExitCode);
    }
}

/// <summary>NTFS metadata is tested on real Windows; other platforms report this limitation as explicit skips.</summary>
public sealed class WindowsTheoryAttribute : TheoryAttribute
{
    public WindowsTheoryAttribute()
    {
        if (!OperatingSystem.IsWindows()) Skip = "Actual Windows NTFS metadata and hardlink fixture required.";
    }
}
