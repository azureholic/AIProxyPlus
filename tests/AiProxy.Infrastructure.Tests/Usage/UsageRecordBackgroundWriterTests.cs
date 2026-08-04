using AiProxy.Application.Abstractions;
using AiProxy.Domain.Entities;
using AiProxy.Domain.Enums;
using AiProxy.Infrastructure.Persistence;
using AiProxy.Infrastructure.Repositories;
using AiProxy.Infrastructure.Tests.Persistence;
using AiProxy.Infrastructure.Usage;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace AiProxy.Infrastructure.Tests.Usage;

[Collection(nameof(PostgresCollection))]
public class UsageRecordBackgroundWriterTests(PostgresContainerFixture fixture)
{
    private static UsageRecord NewUsageRecord(string requestId) =>
        UsageRecord.Create(
            Guid.NewGuid(), "gpt-4o-prod", Guid.NewGuid(), "team-checkout", Guid.NewGuid(), "prod-endpoint",
            Provider.OpenAI, OperationType.ChatCompletion, requestId, streaming: false, httpStatusCode: 200,
            durationMs: 50, totalTokens: 5);

    [Fact]
    public async Task EnqueuedRecords_AreEventuallyPersisted_WithoutBlockingTheEnqueueCall()
    {
        var services = new ServiceCollection();
        services.AddDbContext<AiProxyDbContext>(o => o.UseNpgsql(fixture.ConnectionString));
        services.AddScoped<IUsageRecordRepository, UsageRecordRepository>();
        await using var provider = services.BuildServiceProvider();

        var channel = new UsageRecordChannel(Substitute.For<ILogger<UsageRecordChannel>>(), capacity: 1000);
        var writer = new UsageRecordBackgroundWriter(
            channel, provider.GetRequiredService<IServiceScopeFactory>(),
            Substitute.For<ILogger<UsageRecordBackgroundWriter>>());

        using var cts = new CancellationTokenSource();
        await writer.StartAsync(cts.Token);
        try
        {
            const int recordCount = 50;
            for (var i = 0; i < recordCount; i++)
            {
                channel.Enqueue(NewUsageRecord($"load-req-{i}"));
            }

            await using var assertContext = fixture.CreateDbContext();
            var persistedCount = await PollUntilAsync(
                () => assertContext.UsageRecords.CountAsync(u => u.RequestId.StartsWith("load-req-")),
                expected: recordCount,
                timeout: TimeSpan.FromSeconds(10));

            persistedCount.Should().Be(recordCount);
        }
        finally
        {
            await writer.StopAsync(CancellationToken.None);
        }
    }

    private static async Task<int> PollUntilAsync(Func<Task<int>> getCount, int expected, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        var count = 0;
        while (DateTime.UtcNow < deadline)
        {
            count = await getCount();
            if (count >= expected)
            {
                return count;
            }

            await Task.Delay(100);
        }

        return count;
    }
}
