using AIProxyPlus.Application.Abstractions;
using AIProxyPlus.Infrastructure.Observability;
using AIProxyPlus.Infrastructure.Secrets;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace AIProxyPlus.Infrastructure.Tests.Secrets;

public sealed class VaultSecretRetirementQueueTests
{
    [Fact]
    public async Task ScheduleOrphanCleanup_WhenFirstDeleteFails_RetriesUntilDeleted()
    {
        var secretResolver = new FailingOnceSecretResolver();
        using var metrics = new AIProxyPlusMetrics();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var queue = new VaultSecretRetirementQueue(
            secretResolver,
            TimeProvider.System,
            new VaultSecretRetirementOptions
            {
                GracePeriod = TimeSpan.Zero,
                RetryDelay = TimeSpan.FromMilliseconds(10),
                MaxRetryDelay = TimeSpan.FromMilliseconds(10),
            },
            metrics,
            NullLogger<VaultSecretRetirementQueue>.Instance);

        await queue.StartAsync(timeout.Token);
        queue.ScheduleOrphanCleanup(Guid.NewGuid(), "endpoints/test/primary/orphan");

        var deletedRef = await secretResolver.Deleted.WaitAsync(timeout.Token);
        deletedRef.Should().Be("endpoints/test/primary/orphan");
        secretResolver.DeleteAttempts.Should().Be(2);
        await queue.StopAsync(timeout.Token);
    }

    private sealed class FailingOnceSecretResolver : ISecretResolver
    {
        private readonly TaskCompletionSource<string> _deleted = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int DeleteAttempts { get; private set; }
        public Task<string> Deleted => _deleted.Task;

        public Task<string?> TryResolveAsync(string secretRef, CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);

        public Task StoreAsync(string secretRef, string plaintextValue, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task DeleteAsync(string secretRef, CancellationToken cancellationToken = default)
        {
            DeleteAttempts++;
            if (DeleteAttempts == 1)
            {
                throw new InvalidOperationException("Vault unavailable");
            }

            _deleted.TrySetResult(secretRef);
            return Task.CompletedTask;
        }
    }
}