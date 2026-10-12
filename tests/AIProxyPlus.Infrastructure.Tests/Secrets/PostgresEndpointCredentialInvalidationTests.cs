using System.Collections.Concurrent;
using AIProxyPlus.Application.Abstractions;
using AIProxyPlus.Infrastructure.Observability;
using AIProxyPlus.Infrastructure.Secrets;
using AIProxyPlus.Infrastructure.Tests.Persistence;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace AIProxyPlus.Infrastructure.Tests.Secrets;

[Collection(nameof(PostgresCollection))]
public sealed class PostgresEndpointCredentialInvalidationTests(PostgresContainerFixture postgres)
{
    [Fact]
    public async Task PublishAsync_WithTwoListeners_InvalidatesBothReplicaCaches()
    {
        const string secretRef = "endpoints/test/secondary/version";
        using var metrics = new AIProxyPlusMetrics();
        var firstCache = new SignalingCredentialCache(secretRef);
        var secondCache = new SignalingCredentialCache(secretRef);
        using var firstListener = CreateListener(firstCache, metrics);
        using var secondListener = CreateListener(secondCache, metrics);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var publisher = new PostgresEndpointCredentialInvalidationPublisher(
            postgres.ConnectionString,
            metrics,
            NullLogger<PostgresEndpointCredentialInvalidationPublisher>.Instance);

        await firstListener.StartAsync(timeout.Token);
        await secondListener.StartAsync(timeout.Token);
        await Task.WhenAll(firstListener.Listening, secondListener.Listening)
            .WaitAsync(TimeSpan.FromSeconds(10), timeout.Token);

        await publisher.PublishAsync(
            new EndpointCredentialInvalidation(Guid.NewGuid(), new[] { secretRef }),
            timeout.Token);

        await Task.WhenAll(firstCache.Invalidated, secondCache.Invalidated)
            .WaitAsync(TimeSpan.FromSeconds(10), timeout.Token);
        firstCache.SecretRefs.Should().ContainSingle().Which.Should().Be(secretRef);
        secondCache.SecretRefs.Should().ContainSingle().Which.Should().Be(secretRef);

        await firstListener.StopAsync(timeout.Token);
        await secondListener.StopAsync(timeout.Token);
    }

    private PostgresEndpointCredentialInvalidationListener CreateListener(
        IEndpointCredentialCache cache,
        AIProxyPlusMetrics metrics) =>
        new(
            postgres.ConnectionString,
            cache,
            metrics,
            NullLogger<PostgresEndpointCredentialInvalidationListener>.Instance);

    private sealed class SignalingCredentialCache(string expectedSecretRef) : IEndpointCredentialCache
    {
        private readonly TaskCompletionSource _invalidated = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ConcurrentBag<string> SecretRefs { get; } = [];
        public Task Invalidated => _invalidated.Task;

        public void Invalidate(string secretRef)
        {
            SecretRefs.Add(secretRef);
            if (secretRef == expectedSecretRef)
            {
                _invalidated.TrySetResult();
            }
        }
    }
}