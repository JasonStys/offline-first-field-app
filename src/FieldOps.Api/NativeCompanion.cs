// File: NativeCompanion.cs
// Purpose: Own a durable, isolated instance of the real sync API for one native OS owner.
// Symbols and line locations: see docs/code-index.md.
// State: profile ownership, ephemeral capability and port are retained until real HTTP/store work ends.

using System.Net;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

namespace FieldOps.Api;

/// <summary>Explicit local-companion mode; it never adopts a service listening on another port.</summary>
public sealed class NativeCompanion : IAsyncDisposable
{
    private const string MarkerPrefix = "fieldops-native-companion-v1:";
    private readonly FileStream ownership;
    private readonly SyncServerStore store;
    private readonly WebApplication app;
    private readonly string token = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
    private bool stopped;
    public Uri Address { get; private set; } = null!;
    public Guid InstanceId { get; private set; }
    public long CurrentCursor => store.GetDiagnostics().CurrentCursor;

    private NativeCompanion(FileStream ownership, SyncServerStore store)
    {
        this.ownership = ownership;
        this.store = store;
        app = ApiBootstrap.Build([], store, token);
    }

    /// <summary>Identify/lock local state before starting Kestrel on a fresh owned loopback port.</summary>
    public static async Task<NativeCompanion> StartAsync(string directory)
    {
        var profile = Path.GetFullPath(directory);
        for (var part = new DirectoryInfo(profile); part is not null; part = part.Parent)
            if (part.Exists && (part.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Linked native companion directories are unsupported.");
        Directory.CreateDirectory(profile);
        var marker = Path.Combine(profile, "profile.marker");
        var checkpointPath = Path.Combine(profile, "checkpoint.json");
        var existing = Directory.GetFileSystemEntries(profile);
        if (existing.Length == 0)
        {
            new NativeCheckpoint(checkpointPath).Save(new NativeServerState(1, [], [], [], 0));
            using var output = new FileStream(marker, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            output.Write(System.Text.Encoding.UTF8.GetBytes(MarkerPrefix + Guid.NewGuid().ToString("N") + "\n"));
            output.Flush(flushToDisk: true);
        }
        var identity = ReadMarker(marker);
        if (!identity.StartsWith(MarkerPrefix, StringComparison.Ordinal) || !identity.EndsWith('\n')
            || !Guid.TryParseExact(identity[MarkerPrefix.Length..^1], "N", out var instance)
            || instance == Guid.Empty || !File.Exists(checkpointPath))
            throw new InvalidDataException("Native state is unidentified or incomplete; no reset attempted.");
        foreach (var path in new[] { checkpointPath, Path.Combine(profile, "owner.lock") })
            if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Linked companion state files are unsupported.");
        var owner = new FileStream(Path.Combine(profile, "owner.lock"), FileMode.OpenOrCreate,
            FileAccess.ReadWrite, FileShare.None);
        NativeCompanion? companion = null;
        try
        {
            var persistent = new SyncServerStore(TimeProvider.System, new NativeCheckpoint(checkpointPath));
            companion = new NativeCompanion(owner, persistent);
            companion.InstanceId = instance;
            await companion.app.StartAsync().ConfigureAwait(false);
            var addresses = companion.app.Services.GetRequiredService<IServer>()
                .Features.Get<IServerAddressesFeature>()?.Addresses;
            if (addresses is null || addresses.Count != 1
                || !Uri.TryCreate(addresses.Single(), UriKind.Absolute, out var uri)
                || uri.Scheme != "http" || uri.Host != "127.0.0.1" || uri.Port <= 0)
                throw new InvalidOperationException("Native listener ownership could not be verified.");
            companion.Address = uri;
            return companion;
        }
        catch
        {
            if (companion is not null) await companion.DisposeAsync().ConfigureAwait(false);
            else await owner.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static string ReadMarker(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Linked companion markers are unsupported.");
        using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var bytes = new byte[128];
        var count = source.Read(bytes);
        if (count != source.Length || count >= bytes.Length)
            throw new InvalidDataException("Native marker exceeds its bound.");
        return System.Text.Encoding.UTF8.GetString(bytes, 0, count);
    }

    /// <summary>Give the native gateway an owned, nonredirecting/nonproxy five-second transport.</summary>
    public HttpClient CreateClient()
    {
        ObjectDisposedException.ThrowIf(stopped, this);
        var client = new HttpClient(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false })
        { BaseAddress = Address, Timeout = TimeSpan.FromSeconds(5) };
        client.DefaultRequestHeaders.Add("X-FieldOps-Native", token);
        return client;
    }

    /// <summary>Drain actual HTTP work and synchronous store commits before releasing ownership.</summary>
    public async ValueTask DisposeAsync()
    {
        if (stopped) return;
        await app.StopAsync(CancellationToken.None).ConfigureAwait(false);
        store.WaitForIdle();
        await app.DisposeAsync().ConfigureAwait(false);
        await ownership.DisposeAsync().ConfigureAwait(false);
        stopped = true;
    }
}
