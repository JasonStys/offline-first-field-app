// File: SyncApiTests.cs
// Purpose: Verify HTTP validation, idempotency, cursor deltas, and concurrent version conflicts.
// Symbols and line locations: see docs/code-index.md.
// Important fixtures: each test owns a fresh in-memory server through WebApplicationFactory.

using System.Net;
using System.Net.Http.Json;
using System.Text;
using FieldOps.Core;
using Microsoft.AspNetCore.Mvc.Testing;

namespace FieldOps.Api.Tests;

public sealed class SyncApiTests
{
    [Fact]
    public async Task Health_ReturnsHealthyStatus()
    {
        await using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/healthz");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("healthy", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Push_ReplaysIdempotentResultAndPublishesOneDelta()
    {
        await using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();
        var mutation = CreateMutation(Guid.NewGuid(), Guid.NewGuid());

        var first = await PostAsync(client, new PushRequest([mutation]));
        var second = await PostAsync(client, new PushRequest([mutation]));
        var pulled = await client.GetFromJsonAsync<PullResponse>("/api/sync/pull?after=0&limit=10");

        Assert.True(first[0].Applied);
        Assert.Equal(first[0], second[0]);
        Assert.NotNull(pulled);
        Assert.Single(pulled.Records);
        Assert.Equal(1, pulled.NextCursor);
    }

    [Fact]
    public async Task Push_ReturnsCurrentServerRecordForVersionConflict()
    {
        await using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();
        var recordId = Guid.NewGuid();
        var first = CreateMutation(Guid.NewGuid(), recordId);
        await PostAsync(client, new PushRequest([first]));
        var stale = CreateMutation(Guid.NewGuid(), recordId) with
        {
            Record = first.Record with { Notes = "Stale local edit" },
        };

        var result = await PostAsync(client, new PushRequest([stale]));

        Assert.False(result[0].Applied);
        Assert.Equal("conflict", result[0].Code);
        Assert.Equal(1, result[0].ServerRecord!.Version);
    }

    [Fact]
    public async Task Push_ConcurrentNewRecordMutationsAcceptsExactlyOneWriter()
    {
        await using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();
        var recordId = Guid.NewGuid();
        var tasks = Enumerable.Range(0, 12)
            .Select(
                index => PostAsync(
                    client,
                    new PushRequest(
                    [
                        CreateMutation(Guid.NewGuid(), recordId) with
                        {
                            Record = CreateMutation(Guid.NewGuid(), recordId).Record with
                            {
                                Title = $"Writer {index}",
                            },
                        },
                    ])))
            .ToArray();

        var responses = await Task.WhenAll(tasks);

        var items = responses.SelectMany(batch => batch).ToArray();
        Assert.Single(items, item => item.Applied);
        Assert.Equal(11, items.Count(item => item.Code == "conflict"));
    }

    [Fact]
    public async Task Push_RejectsUnknownJsonMembers()
    {
        await using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();
        using var content = new StringContent(
            "{\"mutations\":[],\"unexpected\":true}",
            Encoding.UTF8,
            "application/json");

        using var response = await client.PostAsync("/api/sync/push", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/sync/pull?after=-1&limit=10")]
    [InlineData("/api/sync/pull?after=0&limit=0")]
    [InlineData("/api/sync/pull?after=0&limit=201")]
    public async Task Pull_RejectsInvalidBounds(string path)
    {
        await using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Push_RejectsEmptyBatchWithValidationProblem()
    {
        await using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/sync/push", new PushRequest([]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("mutations", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Diagnostics_ReturnsCountsWithoutRecordContent()
    {
        await using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();
        var mutation = CreateMutation(Guid.NewGuid(), Guid.NewGuid());
        await PostAsync(client, new PushRequest([mutation]));

        var json = await client.GetStringAsync("/api/diagnostics");

        Assert.Contains("recordCount", json, StringComparison.Ordinal);
        Assert.DoesNotContain("Synthetic notes", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Push_RejectsUnknownNonzeroRecordVersion()
    {
        await using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();
        var mutation = CreateMutation(Guid.NewGuid(), Guid.NewGuid()) with { ExpectedVersion = 9 };

        var result = await PostAsync(client, new PushRequest([mutation]));

        Assert.Equal("unknown_record_version", result[0].Code);
    }

    private static async Task<IReadOnlyList<PushItemResult>> PostAsync(
        HttpClient client,
        PushRequest request)
    {
        using var response = await client.PostAsJsonAsync("/api/sync/push", request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<IReadOnlyList<PushItemResult>>()
            ?? throw new InvalidDataException("Push test response was empty.");
    }

    private static InspectionMutation CreateMutation(Guid mutationId, Guid recordId)
    {
        var record = InspectionSnapshot.Create(
            recordId,
            "fixture-v1",
            "Generated inspection",
            "Synthetic notes",
            InspectionStatus.Draft,
            0,
            DateTimeOffset.UnixEpoch).Value!;
        return new InspectionMutation(mutationId, "test-device", 0, record);
    }
}
