using System.Threading.Channels;
using AIProxyPlus.Application.Abstractions;
using AIProxyPlus.Infrastructure.Observability;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AIProxyPlus.Infrastructure.Secrets;

public sealed class VaultSecretRetirementOptions
{
    public TimeSpan GracePeriod { get; init; } = TimeSpan.FromHours(1);
    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromSeconds(5);
    public TimeSpan MaxRetryDelay { get; init; } = TimeSpan.FromMinutes(5);
}

public sealed class VaultSecretRetirementQueue(
    ISecretResolver secretResolver,
    TimeProvider timeProvider,
    VaultSecretRetirementOptions options,
    AIProxyPlusMetrics metrics,
    ILogger<VaultSecretRetirementQueue> logger)
    : BackgroundService, IEndpointCredentialRetirementQueue
{
    private readonly Channel<RetirementWorkItem> _queue = Channel.CreateUnbounded<RetirementWorkItem>(
        new UnboundedChannelOptions { SingleReader = true });

    public void ScheduleRetirement(Guid endpointId, string secretRef) =>
        Schedule(endpointId, secretRef, timeProvider.GetUtcNow().Add(options.GracePeriod), "retirement");

    public void ScheduleOrphanCleanup(Guid endpointId, string secretRef) =>
        Schedule(endpointId, secretRef, timeProvider.GetUtcNow(), "orphan");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var workItem in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            _ = RetireAsync(workItem, stoppingToken);
        }
    }

    private void Schedule(Guid endpointId, string secretRef, DateTimeOffset notBefore, string reason)
    {
        _queue.Writer.TryWrite(new RetirementWorkItem(endpointId, secretRef, notBefore, reason, 0));
        metrics.RecordCredentialRetirement(reason, "scheduled");
    }

    private async Task RetireAsync(RetirementWorkItem workItem, CancellationToken cancellationToken)
    {
        var delay = workItem.NotBefore - timeProvider.GetUtcNow();
        if (delay > TimeSpan.Zero)
        {
            await Task.Delay(delay, timeProvider, cancellationToken);
        }

        try
        {
            await secretResolver.DeleteAsync(workItem.SecretRef, cancellationToken);
            metrics.RecordCredentialRetirement(workItem.Reason, "succeeded");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            metrics.RecordCredentialRetirement(workItem.Reason, "failed");
            logger.LogWarning(
                exception,
                "Credential cleanup failed for endpoint {EndpointId}; retrying",
                workItem.EndpointId);
            var retryDelay = TimeSpan.FromTicks(Math.Min(
                options.RetryDelay.Ticks * (1L << Math.Min(workItem.Attempt, 10)),
                options.MaxRetryDelay.Ticks));
            _queue.Writer.TryWrite(workItem with
            {
                NotBefore = timeProvider.GetUtcNow().Add(retryDelay),
                Attempt = workItem.Attempt + 1,
            });
        }
    }

    private sealed record RetirementWorkItem(
        Guid EndpointId,
        string SecretRef,
        DateTimeOffset NotBefore,
        string Reason,
        int Attempt);
}