using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using AIProxyPlus.Api.Tests.Infrastructure;
using AIProxyPlus.Domain.Entities;
using AIProxyPlus.Domain.Enums;
using FluentAssertions;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace AIProxyPlus.Api.Tests.Routing;

public class LoadBalancingTests : IAsyncLifetime, IDisposable
{
    private readonly ApiTestFactory _factory = new();
    private WireMockServer _backend1 = null!;
    private WireMockServer _backend2 = null!;

    public Task InitializeAsync()
    {
        _backend1 = WireMockServer.Start();
        _backend2 = WireMockServer.Start();
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _backend1.Stop();
        _backend2.Stop();
        return Task.CompletedTask;
    }

    public void Dispose() => _factory.Dispose();

    private static ModelEndpoint NewApiKeyEndpoint(Guid modelId, string baseUrl, int priority, int maxRetries = 0, int timeoutSeconds = 100)
    {
        var endpointId = Guid.NewGuid();
        var endpoint = Endpoint.Create(
            endpointId, $"load-balancing-test-endpoint-{endpointId:N}", baseUrl, AuthMode.ApiKey,
            secretRef: "endpoints/x", maxRetries: maxRetries, timeoutSeconds: timeoutSeconds);
        var route = ApiRoute.Create(Guid.NewGuid(), "Native OpenAI", Provider.OpenAI, "/openai/v1");
        return ModelEndpoint.Create(Guid.NewGuid(), modelId, endpoint, route, priority, enabled: true);
    }

    private static object ChatBody(string model) => new { model, messages = new[] { new { role = "user", content = "hi" } } };

    [Fact]
    public async Task SamePriorityEndpoints_SplitTrafficAcrossRepeatedCalls()
    {
        var modelName = $"gpt-4o-{Guid.NewGuid():N}";
        var model = Model.Create(Guid.NewGuid(), modelName);
        model.AddEndpoint(NewApiKeyEndpoint(model.Id, _backend1.Url!, priority: 1));
        model.AddEndpoint(NewApiKeyEndpoint(model.Id, _backend2.Url!, priority: 1));
        _factory.Models.Seed(model);

        _backend1.Given(Request.Create().WithPath("/openai/v1/chat/completions").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithBodyAsJson(new { id = "resp-1" }));
        _backend2.Given(Request.Create().WithPath("/openai/v1/chat/completions").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithBodyAsJson(new { id = "resp-2" }));

        var client = _factory.CreateClient();
        for (var i = 0; i < 8; i++)
        {
            var response = await client.PostAsJsonAsync("/openai/v1/chat/completions", ChatBody(modelName));
            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        // Round-robin across two equal-priority endpoints over 8 requests must hit both, not just one.
        _backend1.LogEntries.Should().NotBeEmpty();
        _backend2.LogEntries.Should().NotBeEmpty();
        (_backend1.LogEntries.Count() + _backend2.LogEntries.Count()).Should().Be(8);
    }

    [Fact]
    public async Task HigherPriorityEndpoint_IsAlwaysPreferredOverLowerPriority_DespiteRoundRobinState()
    {
        var modelName = $"gpt-4o-{Guid.NewGuid():N}";
        var model = Model.Create(Guid.NewGuid(), modelName);
        model.AddEndpoint(NewApiKeyEndpoint(model.Id, _backend1.Url!, priority: 1));
        model.AddEndpoint(NewApiKeyEndpoint(model.Id, _backend2.Url!, priority: 2));
        _factory.Models.Seed(model);

        _backend1.Given(Request.Create().WithPath("/openai/v1/chat/completions").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithBodyAsJson(new { id = "resp-1" }));
        _backend2.Given(Request.Create().WithPath("/openai/v1/chat/completions").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithBodyAsJson(new { id = "resp-2" }));

        var client = _factory.CreateClient();
        for (var i = 0; i < 5; i++)
        {
            await client.PostAsJsonAsync("/openai/v1/chat/completions", ChatBody(modelName));
        }

        _backend1.LogEntries.Should().HaveCount(5);
        _backend2.LogEntries.Should().BeEmpty();
    }

    [Fact]
    public async Task Backend429_TriggersFailoverToNextPriorityEndpoint_SameAs500()
    {
        var modelName = $"gpt-4o-{Guid.NewGuid():N}";
        var model = Model.Create(Guid.NewGuid(), modelName);
        model.AddEndpoint(NewApiKeyEndpoint(model.Id, _backend1.Url!, priority: 1));
        model.AddEndpoint(NewApiKeyEndpoint(model.Id, _backend2.Url!, priority: 2));
        _factory.Models.Seed(model);

        _backend1.Given(Request.Create().WithPath("/openai/v1/chat/completions").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(429).WithHeader("Retry-After", "1"));
        _backend2.Given(Request.Create().WithPath("/openai/v1/chat/completions").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithBodyAsJson(new { id = "resp-2" }));

        var response = await _factory.CreateClient().PostAsJsonAsync("/openai/v1/chat/completions", ChatBody(modelName));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("resp-2");
    }

    [Fact]
    public async Task AllEndpointsReturn429_ReturnsFirstEndpointsOriginal429WithRetryAfterHeader()
    {
        // Every endpoint failing must not be masked behind a generic 503 — the client gets the priority-1
        // endpoint's actual 429 back, including its Retry-After header, so it knows how long to back off
        // (architecture.md §9).
        var modelName = $"gpt-4o-{Guid.NewGuid():N}";
        var model = Model.Create(Guid.NewGuid(), modelName);
        model.AddEndpoint(NewApiKeyEndpoint(model.Id, _backend1.Url!, priority: 1));
        model.AddEndpoint(NewApiKeyEndpoint(model.Id, _backend2.Url!, priority: 2));
        _factory.Models.Seed(model);

        _backend1.Given(Request.Create().WithPath("/openai/v1/chat/completions").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(429).WithHeader("Retry-After", "37"));
        _backend2.Given(Request.Create().WithPath("/openai/v1/chat/completions").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(429));

        var response = await _factory.CreateClient().PostAsJsonAsync("/openai/v1/chat/completions", ChatBody(modelName));

        response.StatusCode.Should().Be((HttpStatusCode)429);
        response.Headers.RetryAfter?.Delta.Should().Be(TimeSpan.FromSeconds(37));
    }

    [Fact]
    public async Task FirstEndpointFails429_SecondEndpointFailsWithADifferentError_ClientSeesFirstEndpoints429()
    {
        // Both failures are replay-safe (429 then 503), so nothing was processed anywhere: the client must see the
        // priority-1 endpoint's 429 + Retry-After, not priority-2's 503 and not a generic error.
        var modelName = $"gpt-4o-{Guid.NewGuid():N}";
        var model = Model.Create(Guid.NewGuid(), modelName);
        model.AddEndpoint(NewApiKeyEndpoint(model.Id, _backend1.Url!, priority: 1));
        model.AddEndpoint(NewApiKeyEndpoint(model.Id, _backend2.Url!, priority: 2));
        _factory.Models.Seed(model);

        _backend1.Given(Request.Create().WithPath("/openai/v1/chat/completions").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(429).WithHeader("Retry-After", "5").WithBodyAsJson(new { error = "rate limited" }));
        _backend2.Given(Request.Create().WithPath("/openai/v1/chat/completions").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(503).WithBodyAsJson(new { error = "backend2 unavailable" }));

        var response = await _factory.CreateClient().PostAsJsonAsync("/openai/v1/chat/completions", ChatBody(modelName));

        response.StatusCode.Should().Be((HttpStatusCode)429);
        response.Headers.RetryAfter?.Delta.Should().Be(TimeSpan.FromSeconds(5));
        (await response.Content.ReadAsStringAsync()).Should().Contain("rate limited");
    }

    [Fact]
    public async Task FirstEndpointFails429_ThenSecondEndpointReturns500_ClientSeesTheLatestPossiblyProcessedError()
    {
        // Priority-2 may have processed the request before failing, so a stale 429 + Retry-After from priority-1
        // would invite a confident retry of work that may already be done. The client sees priority-2's own error.
        var modelName = $"gpt-4o-{Guid.NewGuid():N}";
        var model = Model.Create(Guid.NewGuid(), modelName);
        model.AddEndpoint(NewApiKeyEndpoint(model.Id, _backend1.Url!, priority: 1));
        model.AddEndpoint(NewApiKeyEndpoint(model.Id, _backend2.Url!, priority: 2));
        _factory.Models.Seed(model);

        _backend1.Given(Request.Create().WithPath("/openai/v1/chat/completions").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(429).WithHeader("Retry-After", "5").WithBodyAsJson(new { error = "rate limited" }));
        _backend2.Given(Request.Create().WithPath("/openai/v1/chat/completions").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(500).WithBodyAsJson(new { error = "backend2 exploded" }));

        var response = await _factory.CreateClient().PostAsJsonAsync("/openai/v1/chat/completions", ChatBody(modelName));

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        response.Headers.RetryAfter.Should().BeNull();
        (await response.Content.ReadAsStringAsync()).Should().Contain("backend2 exploded");
    }

    [Fact]
    public async Task ErrorResponseCutOffMidBody_IsStillHandledAsAPossiblyProcessedFailure()
    {
        // The server announces a 500 with a 100-byte body, sends 7 bytes, then closes. Reading that body fails, but
        // the failure must still be handled (not replayed, endpoint marked unhealthy) instead of escaping as a crash.
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

                Interlocked.Increment(ref acceptedConnections);
                using (client)
                {
                    var stream = client.GetStream();
                    await stream.ReadAsync(new byte[4096]);
                    await stream.WriteAsync(
                        "HTTP/1.1 500 Internal Server Error\r\nContent-Type: text/plain\r\nContent-Length: 100\r\n\r\npartial"u8.ToArray());
                }
            }
        });

        try
        {
            var modelName = $"gpt-4o-{Guid.NewGuid():N}";
            var model = Model.Create(Guid.NewGuid(), modelName);
            model.AddEndpoint(NewApiKeyEndpoint(model.Id, $"http://127.0.0.1:{port}", priority: 1, maxRetries: 3));
            model.AddEndpoint(NewApiKeyEndpoint(model.Id, _backend2.Url!, priority: 2));
            _factory.Models.Seed(model);

            _backend2.Given(Request.Create().WithPath("/openai/v1/chat/completions").UsingPost())
                .RespondWith(Response.Create().WithStatusCode(200).WithBodyAsJson(new { id = "resp-2", choices = Array.Empty<object>() }));

            var first = await _factory.CreateClient().PostAsJsonAsync("/openai/v1/chat/completions", ChatBody(modelName));

            first.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
            acceptedConnections.Should().Be(1, "the same endpoint must not be retried");
            _backend2.LogEntries.Should().BeEmpty("the request must not be failed over to another endpoint");

            var second = await _factory.CreateClient().PostAsJsonAsync("/openai/v1/chat/completions", ChatBody(modelName));

            second.StatusCode.Should().Be(HttpStatusCode.OK, "the failed endpoint was marked unhealthy, so the next request skips it");
            acceptedConnections.Should().Be(1);
        }
        finally
        {
            listener.Stop();
            await serverTask;
        }
    }

    [Fact]
    public async Task CallerHangsUpWhileReadingAnAmbiguousErrorBody_StillMarksTheEndpointUnhealthy()
    {
        // The upstream answers 500 and then stalls mid-body. The caller gives up while the proxy is reading it. The
        // endpoint's 5xx is already known, so the cancellation must not erase it: the next request skips the endpoint.
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var acceptedConnections = 0;
        var release = new TaskCompletionSource();
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

                Interlocked.Increment(ref acceptedConnections);
                _ = Task.Run(async () =>
                {
                    using (client)
                    {
                        var stream = client.GetStream();
                        await stream.ReadAsync(new byte[4096]);
                        await stream.WriteAsync(
                            "HTTP/1.1 500 Internal Server Error\r\nContent-Type: text/plain\r\nContent-Length: 1000\r\n\r\npartial"u8.ToArray());
                        await Task.WhenAny(release.Task, Task.Delay(TimeSpan.FromSeconds(10)));
                    }
                });
            }
        });

        try
        {
            var modelName = $"gpt-4o-{Guid.NewGuid():N}";
            var model = Model.Create(Guid.NewGuid(), modelName);
            model.AddEndpoint(NewApiKeyEndpoint(model.Id, $"http://127.0.0.1:{port}", priority: 1));
            model.AddEndpoint(NewApiKeyEndpoint(model.Id, _backend2.Url!, priority: 2));
            _factory.Models.Seed(model);

            _backend2.Given(Request.Create().WithPath("/openai/v1/chat/completions").UsingPost())
                .RespondWith(Response.Create().WithStatusCode(200).WithBodyAsJson(new { id = "resp-2", choices = Array.Empty<object>() }));

            using var client = _factory.CreateClient();
            using var cancellation = new CancellationTokenSource();
            var firstRequest = client.PostAsJsonAsync("/openai/v1/chat/completions", ChatBody(modelName), cancellation.Token);
            for (var attempt = 0; attempt < 100 && Volatile.Read(ref acceptedConnections) == 0; attempt++)
            {
                await Task.Delay(20);
            }

            await Task.Delay(300);
            cancellation.Cancel();
            await Task.WhenAny(firstRequest);
            release.TrySetResult();
            await Task.Delay(500);

            var second = await client.PostAsJsonAsync("/openai/v1/chat/completions", ChatBody(modelName));

            second.StatusCode.Should().Be(HttpStatusCode.OK, "the failed endpoint was marked unhealthy despite the caller hanging up");
            acceptedConnections.Should().Be(1, "the unhealthy endpoint must not be contacted again");
        }
        finally
        {
            release.TrySetResult();
            listener.Stop();
            await serverTask;
        }
    }
    [Fact]
    public async Task UpstreamThatNeverAnswers_TimesOutAtTheEndpointTimeout_WithoutRetryOrFailover()
    {
        // The request was sent and then nothing came back. The upstream may still be processing it, so the timeout
        // is ambiguous: no retry, no failover, and the endpoint is skipped by the next request.
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var acceptedConnections = 0;
        var release = new TaskCompletionSource();
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

                Interlocked.Increment(ref acceptedConnections);
                // Each connection stalls on its own task so the loop keeps accepting and counting every attempt.
                _ = Task.Run(async () =>
                {
                    using (client)
                    {
                        await client.GetStream().ReadAsync(new byte[4096]);
                        await Task.WhenAny(release.Task, Task.Delay(TimeSpan.FromSeconds(30)));
                    }
                });
            }
        });

        try
        {
            var modelName = $"gpt-4o-{Guid.NewGuid():N}";
            var model = Model.Create(Guid.NewGuid(), modelName);
            model.AddEndpoint(NewApiKeyEndpoint(model.Id, $"http://127.0.0.1:{port}", priority: 1, maxRetries: 3, timeoutSeconds: 1));
            model.AddEndpoint(NewApiKeyEndpoint(model.Id, _backend2.Url!, priority: 2));
            _factory.Models.Seed(model);

            _backend2.Given(Request.Create().WithPath("/openai/v1/chat/completions").UsingPost())
                .RespondWith(Response.Create().WithStatusCode(200).WithBodyAsJson(new { id = "resp-2", choices = Array.Empty<object>() }));

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var first = await _factory.CreateClient().PostAsJsonAsync("/openai/v1/chat/completions", ChatBody(modelName));
            stopwatch.Stop();

            first.IsSuccessStatusCode.Should().BeFalse();
            stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10), "the 1 second endpoint timeout must apply");
            acceptedConnections.Should().Be(1, "the same endpoint must not be retried");
            _backend2.LogEntries.Should().BeEmpty("the request must not be failed over to another endpoint");

            var second = await _factory.CreateClient().PostAsJsonAsync("/openai/v1/chat/completions", ChatBody(modelName));

            second.StatusCode.Should().Be(HttpStatusCode.OK, "the timed-out endpoint was marked unhealthy");
            acceptedConnections.Should().Be(1);
        }
        finally
        {
            release.TrySetResult();
            listener.Stop();
            await serverTask;
        }
    }

    [Fact]
    public async Task ResponseBodyArrivingAfterTheEndpointTimeout_IsNotCutOff()
    {
        // The timeout bounds the wait for response headers only. Headers arrive at once; the body trickles in later.
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var serverTask = Task.Run(async () =>
        {
            try
            {
                using var client = await listener.AcceptTcpClientAsync();
                var stream = client.GetStream();
                await stream.ReadAsync(new byte[4096]);
                await stream.WriteAsync(
                    "HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: 13\r\n\r\n"u8.ToArray());
                await Task.Delay(TimeSpan.FromSeconds(2));
                await stream.WriteAsync("{\"id\":\"late\"}"u8.ToArray());
            }
            catch (Exception)
            {
            }
        });

        try
        {
            var modelName = $"gpt-4o-{Guid.NewGuid():N}";
            var model = Model.Create(Guid.NewGuid(), modelName);
            model.AddEndpoint(NewApiKeyEndpoint(model.Id, $"http://127.0.0.1:{port}", priority: 1, timeoutSeconds: 1));
            _factory.Models.Seed(model);

            var response = await _factory.CreateClient().PostAsJsonAsync("/openai/v1/chat/completions", ChatBody(modelName));

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            (await response.Content.ReadAsStringAsync()).Should().Be("{\"id\":\"late\"}", "the whole body must arrive intact");
        }
        finally
        {
            listener.Stop();
            await serverTask;
        }
    }
    [Fact]
    public async Task TransportFailureAfterConnecting_IsNotRetriedOrFailedOver()
    {
        // The server accepts the connection and reads the request, then closes without answering. The request may
        // have been received, so replaying the POST (here or on another endpoint) could duplicate the operation.
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

                Interlocked.Increment(ref acceptedConnections);
                using (client)
                {
                    await client.GetStream().ReadAsync(new byte[4096]);
                }
            }
        });

        try
        {
            var modelName = $"gpt-4o-{Guid.NewGuid():N}";
            var model = Model.Create(Guid.NewGuid(), modelName);
            model.AddEndpoint(NewApiKeyEndpoint(model.Id, $"http://127.0.0.1:{port}", priority: 1, maxRetries: 3));
            model.AddEndpoint(NewApiKeyEndpoint(model.Id, _backend2.Url!, priority: 2));
            _factory.Models.Seed(model);

            _backend2.Given(Request.Create().WithPath("/openai/v1/chat/completions").UsingPost())
                .RespondWith(Response.Create().WithStatusCode(200).WithBodyAsJson(new { id = "resp-2", choices = Array.Empty<object>() }));

            var response = await _factory.CreateClient().PostAsJsonAsync("/openai/v1/chat/completions", ChatBody(modelName));

            response.IsSuccessStatusCode.Should().BeFalse();
            acceptedConnections.Should().Be(1, "the same endpoint must not be retried");
            _backend2.LogEntries.Should().BeEmpty("the request must not be failed over to another endpoint");
        }
        finally
        {
            listener.Stop();
            await serverTask;
        }
    }
}
