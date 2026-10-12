using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using AIProxyPlus.Api.Tests.Infrastructure;
using AIProxyPlus.Application.Abstractions;
using AIProxyPlus.Domain.Entities;
using AIProxyPlus.Domain.Enums;
using FluentAssertions;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace AIProxyPlus.Api.Tests.Routing;

public class CredentialFallbackRoutingTests : IAsyncLifetime, IDisposable
{
    private readonly ApiTestFactory _factory = new();
    private WireMockServer _backend = null!;

    public Task InitializeAsync()
    {
        _backend = WireMockServer.Start();
        _factory.Credentials.Candidates = new EndpointCredentialCandidates(
            new EndpointCredential("Authorization", "Bearer active-key"),
            _ => Task.FromResult(new EndpointCredential("Authorization", "Bearer fallback-key")),
            EndpointCredentialSlot.Secondary,
            EndpointCredentialSlot.Primary);
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _backend.Stop();
        return Task.CompletedTask;
    }

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task ActiveCredentialAccepted_DoesNotTryFallback()
    {
        var modelName = SeedModel();
        StubCredential("Bearer active-key", HttpStatusCode.OK, "active-response");
        StubCredential("Bearer fallback-key", HttpStatusCode.OK, "fallback-response");

        var response = await SendChatAsync(modelName);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("active-response");
        _backend.LogEntries.Should().ContainSingle();
    }

    [Fact]
    public async Task ActiveCredentialRejectedWith401_TriesFallbackOnce()
    {
        var modelName = SeedModel();
        StubCredential("Bearer active-key", HttpStatusCode.Unauthorized, "active-rejected");
        StubCredential("Bearer fallback-key", HttpStatusCode.OK, "fallback-response");

        var response = await SendChatAsync(modelName);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("fallback-response");
        _backend.LogEntries.Should().HaveCount(2);
    }

    [Fact]
    public async Task BothCredentialsRejected_ReturnsOriginalActiveCredentialResponse()
    {
        var modelName = SeedModel();
        StubCredential("Bearer active-key", HttpStatusCode.Unauthorized, "active-rejected");
        StubCredential("Bearer fallback-key", HttpStatusCode.Unauthorized, "fallback-rejected");

        var response = await SendChatAsync(modelName);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await response.Content.ReadAsStringAsync()).Should().Contain("active-rejected");
        _backend.LogEntries.Should().HaveCount(2);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    public async Task FallbackFailsWithPossiblyProcessedError_ReturnsFallbackErrorNotTheEarlier401(HttpStatusCode statusCode)
    {
        // The fallback request may have been processed before failing, so replaying the active-key 401 would hide that
        // and invite a duplicate retry. The caller sees the fallback's own error instead.
        var modelName = SeedModel();
        StubCredential("Bearer active-key", HttpStatusCode.Unauthorized, "active-rejected");
        StubCredential("Bearer fallback-key", statusCode, "fallback-failed");

        var response = await SendChatAsync(modelName);

        response.StatusCode.Should().Be(statusCode);
        (await response.Content.ReadAsStringAsync()).Should().Contain("fallback-failed");
        _backend.LogEntries.Should().HaveCount(2);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task FallbackRejectedBeforeProcessing_ReturnsTheOriginalActiveCredentialResponse(HttpStatusCode statusCode)
    {
        var modelName = SeedModel();
        StubCredential("Bearer active-key", HttpStatusCode.Unauthorized, "active-rejected");
        StubCredential("Bearer fallback-key", statusCode, "fallback-unavailable");

        var response = await SendChatAsync(modelName);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await response.Content.ReadAsStringAsync()).Should().Contain("active-rejected");
        _backend.LogEntries.Should().HaveCount(2);
    }
    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task NonCredentialRejection_DoesNotTryFallback(HttpStatusCode statusCode)
    {
        var modelName = SeedModel();
        StubCredential("Bearer active-key", statusCode, "active-failure");
        StubCredential("Bearer fallback-key", HttpStatusCode.OK, "fallback-response");

        var response = await SendChatAsync(modelName);

        response.StatusCode.Should().Be(statusCode);
        _backend.LogEntries.Should().ContainSingle();
    }

    [Fact]
    public async Task FallbackConnectionResetAfterSending_IsNotMaskedByTheEarlier401_AndMarksTheEndpointUnhealthy()
    {
        // The first request is answered 401 (active key rejected); the fallback request is read and then the
        // connection is reset without a response, so it may have been processed. The caller must not be told
        // "401" (inviting a duplicate retry), nothing may fail over for this request, and the endpoint must be
        // skipped by the next one.
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var acceptedConnections = 0;
        var serverTask = Task.Run(async () =>
        {
            while (true)
            {
                TcpClient client;
                try
                {
                    client = await listener.AcceptTcpClientAsync();
                }
                catch (Exception)
                {
                    return;
                }

                var connectionNumber = Interlocked.Increment(ref acceptedConnections);
                using (client)
                {
                    var stream = client.GetStream();
                    await stream.ReadAsync(new byte[8192]);
                    if (connectionNumber == 1)
                    {
                        await stream.WriteAsync(
                            "HTTP/1.1 401 Unauthorized\r\nConnection: close\r\nContent-Length: 0\r\n\r\n"u8.ToArray());
                    }
                }
            }
        });

        try
        {
            var modelName = $"credential-fallback-{Guid.NewGuid():N}";
            var model = Model.Create(Guid.NewGuid(), modelName);
            model.AddEndpoint(NewEndpoint(model.Id, $"http://127.0.0.1:{port}", priority: 1));
            model.AddEndpoint(NewEndpoint(model.Id, _backend.Url!, priority: 2));
            _factory.Models.Seed(model);
            StubCredential("Bearer active-key", HttpStatusCode.OK, "second-endpoint-response");

            var first = await SendChatAsync(modelName);

            first.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized, "the earlier 401 must not mask a possibly-processed request");
            first.IsSuccessStatusCode.Should().BeFalse();
            acceptedConnections.Should().Be(2, "one rejected active-key attempt and one fallback attempt, with no further retry");
            _backend.LogEntries.Should().BeEmpty("the request must not be failed over to another endpoint");

            var second = await SendChatAsync(modelName);

            second.StatusCode.Should().Be(HttpStatusCode.OK, "the failed endpoint was marked unhealthy, so the next request skips it");
            acceptedConnections.Should().Be(2);
        }
        finally
        {
            listener.Stop();
            await serverTask;
        }
    }

    private static ModelEndpoint NewEndpoint(Guid modelId, string baseUrl, int priority)
    {
        var endpoint = Endpoint.Create(
            Guid.NewGuid(), $"endpoint-{Guid.NewGuid():N}", baseUrl, AuthMode.ApiKey,
            secretRef: "endpoints/primary", maxRetries: 0);
        var route = ApiRoute.Create(Guid.NewGuid(), "Native OpenAI", Provider.OpenAI, "/openai/v1");
        return ModelEndpoint.Create(Guid.NewGuid(), modelId, endpoint, route, priority, enabled: true);
    }
    private string SeedModel()
    {
        var modelName = $"credential-fallback-{Guid.NewGuid():N}";
        var model = Model.Create(Guid.NewGuid(), modelName);
        var endpoint = Endpoint.Create(
            Guid.NewGuid(), $"endpoint-{Guid.NewGuid():N}", _backend.Url!, AuthMode.ApiKey,
            secretRef: "endpoints/primary", maxRetries: 0);
        var route = ApiRoute.Create(Guid.NewGuid(), "Native OpenAI", Provider.OpenAI, "/openai/v1");
        model.AddEndpoint(ModelEndpoint.Create(Guid.NewGuid(), model.Id, endpoint, route, 1, enabled: true));
        _factory.Models.Seed(model);
        return modelName;
    }

    private void StubCredential(string authorization, HttpStatusCode statusCode, string responseId)
    {
        _backend
            .Given(Request.Create()
                .WithPath("/openai/v1/chat/completions")
                .UsingPost()
                .WithHeader("Authorization", authorization))
            .RespondWith(Response.Create()
                .WithStatusCode(statusCode)
                .WithBodyAsJson(new { id = responseId, choices = Array.Empty<object>() }));
    }

    private Task<HttpResponseMessage> SendChatAsync(string modelName) =>
        _factory.CreateClient().PostAsJsonAsync(
            "/openai/v1/chat/completions",
            new { model = modelName, messages = new[] { new { role = "user", content = "hi" } } });
}