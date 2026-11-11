// File: NativeAuthorityTests.cs
// Purpose: Prove native pairing preserves legacy SQLite and refuses changed generations/old checkpoints.
// Symbols and line locations: see docs/code-index.md.
// State: independent actual SQLite and regular pairing fixtures under owned temporary directories.

using FieldOps.Core;

namespace FieldOps.Infrastructure.Tests;

public sealed class NativeAuthorityTests
{
    [Fact]
    public async Task LegacyAcceptedDatabase_CannotBeSilentlyPairedOrReset()
    {
        using var workspace = new TempWorkspace();
        var store = new LocalFieldStore(workspace.DatabasePath);
        await store.InitializeAsync();
        var record = InspectionSnapshot.Create(Guid.NewGuid(), "legacy-v1", "Synthetic existing inspection",
            "Retain this accepted source record", InspectionStatus.Completed, 7, DateTimeOffset.UnixEpoch).Value!;
        await store.ApplyRemoteAsync([record], 12);
        var before = await File.ReadAllBytesAsync(workspace.DatabasePath);
        var authority = new NativeAuthority(workspace.Root);
        await Assert.ThrowsAsync<InvalidOperationException>(() => authority.ReadExpectedAsync(store));
        Assert.Equal(before, await File.ReadAllBytesAsync(workspace.DatabasePath));
        Assert.False(File.Exists(Path.Combine(workspace.Root, "native-authority.txt")));
        Assert.Equal(12, await store.GetCursorAsync());
        Assert.Equal(record, Assert.Single(await store.SearchAsync(null, 10)));
    }

    [Fact]
    public async Task NewOfflineDraft_CanPair_ThenGenerationAndCursorMustRemainConsistent()
    {
        using var workspace = new TempWorkspace();
        var store = new LocalFieldStore(workspace.DatabasePath);
        await store.InitializeAsync();
        var record = InspectionSnapshot.Create(Guid.NewGuid(), "fixture-v1", "Synthetic offline draft",
            "Queued but not accepted", InspectionStatus.Draft, 0, DateTimeOffset.UnixEpoch).Value!;
        await store.SaveDraftAsync(record, "fictional-device");
        var authority = new NativeAuthority(workspace.Root);
        Assert.Null(await authority.ReadExpectedAsync(store));
        var instance = Guid.NewGuid();
        await authority.BindAsync(store, null, instance, 0);
        Assert.Equal(instance, await authority.ReadExpectedAsync(store));
        var pair = await File.ReadAllBytesAsync(Path.Combine(workspace.Root, "native-authority.txt"));
        await Assert.ThrowsAsync<InvalidDataException>(() => authority.BindAsync(store, instance, Guid.NewGuid(), 0));
        await Assert.ThrowsAsync<IOException>(() => authority.BindAsync(store, null, instance, 0));
        await store.ApplyRemoteAsync([], 5);
        await Assert.ThrowsAsync<InvalidDataException>(() => authority.BindAsync(store, instance, instance, 4));
        await authority.BindAsync(store, instance, instance, 5);
        Assert.Equal(pair, await File.ReadAllBytesAsync(Path.Combine(workspace.Root, "native-authority.txt")));
        Assert.Equal(record, Assert.Single(await store.SearchAsync(null, 10)));
        Assert.Equal(0, Assert.Single(await store.StartBatchAsync(10)).Mutation.ExpectedVersion);
    }

    [Theory]
    [InlineData("")]
    [InlineData("wrong authority")]
    [InlineData("00000000000000000000000000000000\r\n")]
    public async Task MalformedPairing_IsPreservedInsteadOfReplaced(string bytes)
    {
        using var workspace = new TempWorkspace();
        var store = new LocalFieldStore(workspace.DatabasePath);
        await store.InitializeAsync();
        var path = Path.Combine(workspace.Root, "native-authority.txt");
        await File.WriteAllTextAsync(path, bytes);
        await Assert.ThrowsAsync<InvalidDataException>(() => new NativeAuthority(workspace.Root).ReadExpectedAsync(store));
        Assert.Equal(bytes, await File.ReadAllTextAsync(path));
    }
}
