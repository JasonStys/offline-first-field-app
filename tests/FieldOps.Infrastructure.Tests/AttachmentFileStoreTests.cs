// File: AttachmentFileStoreTests.cs
// Purpose: Verify streaming integrity, safe names, content types, and the hard attachment size limit.
// Symbols and line locations: see docs/code-index.md.
// Important fixture: Payload is synthetic and contains no personal or employer data.

using System.Security.Cryptography;
using FieldOps.Infrastructure;

namespace FieldOps.Infrastructure.Tests;

public sealed class AttachmentFileStoreTests
{
    [Fact]
    public async Task SaveAsync_StreamsPayloadAndReturnsIntegrityMetadata()
    {
        using var workspace = new TempWorkspace();
        var payload = Enumerable.Range(0, 16_384).Select(value => (byte)(value % 251)).ToArray();
        var store = new AttachmentFileStore(workspace.Root);

        var result = await store.SaveAsync(
            Guid.NewGuid(),
            "fixture.png",
            "image/png",
            new MemoryStream(payload));

        Assert.Equal(payload.Length, result.LengthBytes);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(payload)), result.Sha256Hex);
        Assert.True(File.Exists(Path.Combine(workspace.Root, result.RelativePath)));
    }

    [Fact]
    public async Task SaveAsync_RejectsOversizedStreamAndRemovesPartialFile()
    {
        using var workspace = new TempWorkspace();
        var store = new AttachmentFileStore(workspace.Root);
        var payload = new byte[AttachmentFileStore.MaximumAttachmentBytes + 1];

        await Assert.ThrowsAsync<InvalidDataException>(
            () => store.SaveAsync(
                Guid.NewGuid(),
                "too-large.jpg",
                "image/jpeg",
                new MemoryStream(payload)));

        Assert.Empty(Directory.EnumerateFiles(workspace.Root, "*.partial", SearchOption.AllDirectories));
    }

    [Theory]
    [InlineData("application/pdf")]
    [InlineData("text/html")]
    public async Task SaveAsync_RejectsUnsupportedContentType(string contentType)
    {
        using var workspace = new TempWorkspace();
        var store = new AttachmentFileStore(workspace.Root);

        await Assert.ThrowsAsync<ArgumentException>(
            () => store.SaveAsync(
                Guid.NewGuid(),
                "fixture.bin",
                contentType,
                Stream.Null));
    }
}
