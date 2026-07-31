using AiProxy.Application.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AiProxy.Infrastructure.Usage;

/// <summary>Drains <see cref="UsageRecordChannel"/> and persists each record via a fresh DI scope per item.</summary>
public sealed class UsageRecordBackgroundWriter(
    UsageRecordChannel channel,
    IServiceScopeFactory scopeFactory,
    ILogger<UsageRecordBackgroundWriter> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var usageRecord in channel.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var repository = scope.ServiceProvider.GetRequiredService<IUsageRecordRepository>();
                await repository.AddAsync(usageRecord, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(
                    ex,
                    "Failed to persist usage record for request {RequestId} (deployment {ModelDeploymentName}).",
                    usageRecord.RequestId,
                    usageRecord.ModelDeploymentName);
            }
        }
    }
}
