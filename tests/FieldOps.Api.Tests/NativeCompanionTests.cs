// File: NativeCompanionTests.cs
// Purpose: Exercise the actual owned loopback API, durable replay and fail-closed profile behavior.
// Symbols and line locations: see docs/code-index.md.
// Inputs: new fictional temporary profiles only; no normal MAUI data or existing ports are reused.

using System.Net;
using System.Net.Http.Json;
using FieldOps.Core;

namespace FieldOps.Api.Tests;

public sealed class NativeCompanionTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "fieldops-native-test-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task RealOwnedService_RefusesForeignRequests_AndRetainsActualPushReplayAcrossRestart()
    {
        var profile = Path.Combine(root, "profile");
        Directory.CreateDirectory(root);
        var id = Guid.NewGuid();
        var record = InspectionSnapshot.Create(id, "equipment-v1", "Pump A pressure inspection",
            "Synthetic calibration check", InspectionStatus.Draft, 0, DateTimeOffset.UtcNow).Value!;
        var mutation = new InspectionMutation(Guid.NewGuid(), "fictional-device", 0, record);
        Uri firstAddress;
        await using (var companion = await NativeCompanion.StartAsync(profile))
        {
            firstAddress = companion.Address;
            Assert.Equal("127.0.0.1", companion.Address.Host);
            using var client = companion.CreateClient();
            using var healthy = await client.GetAsync("/healthz");
            Assert.Equal(HttpStatusCode.OK, healthy.StatusCode);
            using var anonymous = new HttpClient(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false })
            { BaseAddress = companion.Address, Timeout = TimeSpan.FromSeconds(5) };
            using var denied = await anonymous.GetAsync("/healthz");
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            client.DefaultRequestHeaders.Add("Origin", "https://outside.invalid");
            using var origin = await client.GetAsync("/healthz");
            Assert.Equal(HttpStatusCode.Forbidden, origin.StatusCode);
            client.DefaultRequestHeaders.Remove("Origin");
            client.DefaultRequestHeaders.Host = "outside.invalid";
            using var host = await client.GetAsync("/healthz");
            Assert.Equal(HttpStatusCode.Forbidden, host.StatusCode);
            client.DefaultRequestHeaders.Host = null;
            using var push = await client.PostAsJsonAsync("/api/sync/push", new PushRequest([mutation]));
            push.EnsureSuccessStatusCode();
            var result = await push.Content.ReadFromJsonAsync<PushItemResult[]>();
            Assert.True(Assert.Single(result!).Applied);
            await Assert.ThrowsAsync<IOException>(() => NativeCompanion.StartAsync(profile));
        }
        using (var gone = new HttpClient(new HttpClientHandler { UseProxy = false }))
            await Assert.ThrowsAsync<HttpRequestException>(() => gone.GetAsync(new Uri(firstAddress, "/healthz")));
        await using (var reopened = await NativeCompanion.StartAsync(profile))
        {
            using var client = reopened.CreateClient();
            using var push = await client.PostAsJsonAsync("/api/sync/push", new PushRequest([mutation]));
            push.EnsureSuccessStatusCode();
            var results = await push.Content.ReadFromJsonAsync<PushItemResult[]>();
            Assert.Equal(1, Assert.Single(results!).AcceptedVersion);
            var pulled = await client.GetFromJsonAsync<PullResponse>("/api/sync/pull?after=0&limit=10");
            Assert.Equal(1, pulled!.NextCursor);
            Assert.Equal(id, Assert.Single(pulled.Records).Id);
        }
    }

    [Fact]
    public async Task UnknownOrIncompleteProfile_IsPreservedInsteadOfReset()
    {
        Directory.CreateDirectory(root);
        var unknown = Path.Combine(root, "unknown.txt");
        await File.WriteAllTextAsync(unknown, "Preserve this unrelated file");
        await Assert.ThrowsAsync<FileNotFoundException>(() => NativeCompanion.StartAsync(root));
        Assert.Equal("Preserve this unrelated file", await File.ReadAllTextAsync(unknown));
        Assert.False(File.Exists(Path.Combine(root, "checkpoint.json")));
        var profile = Path.Combine(root, "known");
        await using (var initial = await NativeCompanion.StartAsync(profile)) { }
        File.Move(Path.Combine(profile, "checkpoint.json"), Path.Combine(profile, "retained-checkpoint.json"));
        await Assert.ThrowsAsync<InvalidDataException>(() => NativeCompanion.StartAsync(profile));
        Assert.False(File.Exists(Path.Combine(profile, "checkpoint.json")));
        Assert.True(File.Exists(Path.Combine(profile, "retained-checkpoint.json")));
    }

    public void Dispose()
    {
        var target = Path.GetFullPath(root);
        var parent = Path.GetFullPath(Path.GetTempPath());
        if (!target.StartsWith(parent, StringComparison.OrdinalIgnoreCase)
            || !Path.GetFileName(target).StartsWith("fieldops-native-test-", StringComparison.Ordinal))
            throw new InvalidOperationException("Test cleanup escaped its exact owned prefix.");
        if (Directory.Exists(target)) Directory.Delete(target, recursive: true);
    }
}
