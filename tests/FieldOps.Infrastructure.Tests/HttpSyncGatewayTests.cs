// File: HttpSyncGatewayTests.cs
// Purpose: Verify JSON transport paths, response bounds, and failure behavior without a real network.
// Symbols and line locations: see docs/code-index.md.
// Important fixture: StubHandler returns one deterministic response for each test.

using System.Net;
using System.Text;
using System.Text.Json;
using FieldOps.Core;
using FieldOps.Infrastructure;

namespace FieldOps.Infrastructure.Tests;

public sealed class HttpSyncGatewayTests
{
    [Fact]
    public async Task PushAsync_SendsContractAndReturnsResult()
    {
        var mutation = CreateMutation();
        var expected = new[]
        {
            new PushItemResult(mutation.MutationId, true, "applied", 1, null),
        };
        var handler = new StubHandler(HttpStatusCode.OK, JsonSerializer.Serialize(expected));
        var gateway = CreateGateway(handler);

        var result = await gateway.PushAsync(new PushRequest([mutation]));

        Assert.Equal(expected, result);
        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal("/api/sync/push", handler.Path);
    }

    [Fact]
    public async Task PushAsync_RejectsMismatchedResponseCount()
    {
        var handler = new StubHandler(HttpStatusCode.OK, "[]");
        var gateway = CreateGateway(handler);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => gateway.PushAsync(new PushRequest([CreateMutation()])));
    }

    [Fact]
    public async Task PullAsync_RejectsResponseThatMovesCursorBackward()
    {
        var handler = new StubHandler(
            HttpStatusCode.OK,
            JsonSerializer.Serialize(new PullResponse(2, [])));
        var gateway = CreateGateway(handler);

        await Assert.ThrowsAsync<InvalidDataException>(() => gateway.PullAsync(3, 10));
    }

    [Theory]
    [InlineData(-1, 10)]
    [InlineData(0, 0)]
    [InlineData(0, 201)]
    public async Task PullAsync_RejectsInvalidRequestBounds(long cursor, int limit)
    {
        var gateway = CreateGateway(new StubHandler(HttpStatusCode.OK, "{}"));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => gateway.PullAsync(cursor, limit));
    }

    private static HttpSyncGateway CreateGateway(HttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https://sync.test", UriKind.Absolute) });

    private static InspectionMutation CreateMutation()
    {
        var record = InspectionSnapshot.Create(
            Guid.NewGuid(),
            "fixture-v1",
            "Fixture",
            string.Empty,
            InspectionStatus.Draft,
            0,
            DateTimeOffset.UnixEpoch).Value!;
        return new InspectionMutation(Guid.NewGuid(), "device", 0, record);
    }

    private sealed class StubHandler(HttpStatusCode statusCode, string responseBody) : HttpMessageHandler
    {
        public HttpMethod? Method { get; private set; }
        public string? Path { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Method = request.Method;
            Path = request.RequestUri?.AbsolutePath;
            return Task.FromResult(
                new HttpResponseMessage(statusCode)
                {
                    Content = new StringContent(responseBody, Encoding.UTF8, "application/json"),
                });
        }
    }
}
