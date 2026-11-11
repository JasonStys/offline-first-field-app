// File: NativeCheckpointTests.cs
// Purpose: Prove optional local-companion persistence without changing original sync semantics.
// Symbols and line locations: see docs/code-index.md.
// State: fresh temporary fictional checkpoints; no installed/client user database is accessed.

using System.Text.Json;
using FieldOps.Api;
using FieldOps.Core;

namespace FieldOps.Api.Tests;

public sealed class NativeCheckpointTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "fieldops-native-state-" + Guid.NewGuid().ToString("N"));

    public NativeCheckpointTests() => Directory.CreateDirectory(root);

    private string Checkpoint => Path.Combine(root, "companion.json");

    private static InspectionSnapshot Record() => InspectionSnapshot.Create(Guid.NewGuid(),
        "fixture-v1", "North loading bay", "Synthetic retained checkpoint fixture.",
        InspectionStatus.Draft, 0, DateTimeOffset.UnixEpoch).Value!;

    [Fact]
    public void RealPushReplayHistoryAndConflictSurviveRestart()
    {
        var first = new SyncServerStore(TimeProvider.System, new NativeCheckpoint(Checkpoint));
        var original = Record();
        var create = new InspectionMutation(Guid.NewGuid(), "first-device", 0, original);
        var initial = first.Push(new PushRequest([create])).Single();
        Assert.True(initial.Applied);
        Assert.Equal(1, initial.AcceptedVersion);
        var second = new SyncServerStore(TimeProvider.System, new NativeCheckpoint(Checkpoint));
        Assert.Equal(initial, second.Push(new PushRequest([create])).Single());
        Assert.Equal(1, second.GetDiagnostics().CurrentCursor);
        var retained = second.Pull(0, 10).Records.Single();
        var update = new InspectionMutation(Guid.NewGuid(), "second-device", 1,
            retained with { Notes = "A real second fictional writer.", Version = 1 });
        Assert.Equal(2, second.Push(new PushRequest([update])).Single().AcceptedVersion);
        var stale = new InspectionMutation(Guid.NewGuid(), "first-device", 1,
            retained with { Notes = "Stale local note." });
        var conflict = second.Push(new PushRequest([stale])).Single();
        Assert.Equal("conflict", conflict.Code);
        var third = new SyncServerStore(TimeProvider.System, new NativeCheckpoint(Checkpoint));
        Assert.Equal(conflict, third.Push(new PushRequest([stale])).Single());
        Assert.Equal(2, third.Pull(0, 10).Records.Count);
        Assert.Equal(2, third.Pull(1, 10).Records.Single().Version);
        Assert.Equal("A real second fictional writer.", third.Pull(1, 10).Records.Single().Notes);
        Assert.Equal(new ServerDiagnostics(1, 3, 2, 2), third.GetDiagnostics());
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("{\"Schema\":1,\"Schema\":2}")]
    [InlineData("{\"Schema\":1,\"Records\":[],\"Results\":[],\"Changes\":[],\"NextCursor\":3}")]
    [InlineData("{\"Schema\":1,\"Records\":[null],\"Results\":[],\"Changes\":[],\"NextCursor\":0}")]
    public void CorruptOrAmbiguousCheckpointIsRefusedAndPreserved(string data)
    {
        File.WriteAllText(Checkpoint, data);
        Assert.Throws<InvalidDataException>(() => new SyncServerStore(TimeProvider.System, new NativeCheckpoint(Checkpoint)));
        Assert.Equal(data, File.ReadAllText(Checkpoint));
    }

    [Fact]
    public void FailedPublicationRollsBackMemoryAndRetainsThePriorDestination()
    {
        var store = new SyncServerStore(TimeProvider.System, new NativeCheckpoint(Checkpoint));
        Directory.CreateDirectory(Checkpoint);
        var error = Xunit.Record.Exception(() => store.Push(new PushRequest([
            new InspectionMutation(Guid.NewGuid(), "device", 0, Record())])));
        Assert.True(error is IOException or UnauthorizedAccessException);
        Assert.Equal(new ServerDiagnostics(0, 0, 0, 0), store.GetDiagnostics());
        Assert.True(Directory.Exists(Checkpoint));
        Assert.Empty(Directory.EnumerateFiles(root, "*.tmp"));
    }

    [Fact]
    public void CheckpointHistoryCannotClaimDifferentCurrentRecord()
    {
        var first = Record() with { Version = 1 };
        var wrong = first with { Notes = "A contradictory current snapshot." };
        var state = new NativeServerState(1, [wrong], [], [new NativeServerChange(1, first)], 1);
        File.WriteAllText(Checkpoint, JsonSerializer.Serialize(state));
        var retained = File.ReadAllBytes(Checkpoint);
        Assert.Throws<InvalidDataException>(() => new NativeCheckpoint(Checkpoint).Load());
        Assert.Equal(retained, File.ReadAllBytes(Checkpoint));
    }

    public void Dispose()
    {
        var absolute = Path.GetFullPath(root);
        if (Path.GetDirectoryName(absolute) != Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)
            || !Path.GetFileName(absolute).StartsWith("fieldops-native-state-", StringComparison.Ordinal))
            throw new InvalidOperationException("Unexpected owned fixture cleanup path.");
        Directory.Delete(absolute, recursive: true);
    }
}
