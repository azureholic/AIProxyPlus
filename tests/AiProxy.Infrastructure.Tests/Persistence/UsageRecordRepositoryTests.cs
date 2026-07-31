using AiProxy.Application.Abstractions;
using AiProxy.Domain.Entities;
using AiProxy.Domain.Enums;
using AiProxy.Infrastructure.Repositories;
using FluentAssertions;

namespace AiProxy.Infrastructure.Tests.Persistence;

[Collection(nameof(PostgresCollection))]
public class UsageRecordRepositoryTests(PostgresContainerFixture fixture)
{
    [Fact]
    public async Task AddAsync_Then_Query_RoundTripsTokenFields()
    {
        var deploymentName = $"gpt-4o-{Guid.NewGuid():N}";
        var consumerId = Guid.NewGuid();
        await using var writeContext = fixture.CreateDbContext();
        var record = UsageRecord.Create(
            Guid.NewGuid(), deploymentName, consumerId, "team-checkout", Guid.NewGuid(),
            ApiFamily.OpenAI, OperationType.ChatCompletion, "req-1", streaming: false, httpStatusCode: 200,
            durationMs: 120, promptTokens: 10, completionTokens: 20, totalTokens: 30);
        await new UsageRecordRepository(writeContext).AddAsync(record);

        await using var readContext = fixture.CreateDbContext();
        var result = await new UsageRecordRepository(readContext).QueryAsync(new UsageRecordQuery(ModelDeploymentName: deploymentName));

        result.TotalCount.Should().Be(1);
        result.Items.Single().TotalTokens.Should().Be(30);
        result.Items.Single().UsageMetrics.Should().BeNull();
    }

    [Fact]
    public async Task AddAsync_Then_Query_RoundTripsUsageMetricsDictionary()
    {
        var deploymentName = $"dall-e-{Guid.NewGuid():N}";
        var metrics = new Dictionary<string, string> { ["image_count"] = "2", ["image_size"] = "1024x1024" };
        await using var writeContext = fixture.CreateDbContext();
        var record = UsageRecord.Create(
            Guid.NewGuid(), deploymentName, Guid.NewGuid(), "team-checkout", Guid.NewGuid(),
            ApiFamily.OpenAI, OperationType.ImageGeneration, "req-2", streaming: false, httpStatusCode: 200,
            durationMs: 900, usageMetrics: metrics);
        await new UsageRecordRepository(writeContext).AddAsync(record);

        await using var readContext = fixture.CreateDbContext();
        var result = await new UsageRecordRepository(readContext).QueryAsync(new UsageRecordQuery(ModelDeploymentName: deploymentName));

        result.Items.Single().PromptTokens.Should().BeNull();
        result.Items.Single().UsageMetrics.Should().BeEquivalentTo(metrics);
    }

    [Fact]
    public async Task QueryAsync_FiltersByConsumerNameAndOperationType()
    {
        var deploymentName = $"gpt-4o-{Guid.NewGuid():N}";
        var consumerName = $"team-{Guid.NewGuid():N}";
        await using var writeContext = fixture.CreateDbContext();
        var repository = new UsageRecordRepository(writeContext);
        await repository.AddAsync(UsageRecord.Create(
            Guid.NewGuid(), deploymentName, Guid.NewGuid(), consumerName, Guid.NewGuid(),
            ApiFamily.OpenAI, OperationType.ChatCompletion, "req-a", streaming: false, httpStatusCode: 200,
            durationMs: 100, totalTokens: 1));
        await repository.AddAsync(UsageRecord.Create(
            Guid.NewGuid(), deploymentName, Guid.NewGuid(), "someone-else", Guid.NewGuid(),
            ApiFamily.OpenAI, OperationType.ChatCompletion, "req-b", streaming: false, httpStatusCode: 200,
            durationMs: 100, totalTokens: 1));
        await repository.AddAsync(UsageRecord.Create(
            Guid.NewGuid(), deploymentName, Guid.NewGuid(), consumerName, Guid.NewGuid(),
            ApiFamily.OpenAI, OperationType.Embedding, "req-c", streaming: false, httpStatusCode: 200,
            durationMs: 100, totalTokens: 1));

        await using var readContext = fixture.CreateDbContext();
        var result = await new UsageRecordRepository(readContext).QueryAsync(new UsageRecordQuery(
            ModelDeploymentName: deploymentName, ConsumerName: consumerName, OperationType: OperationType.ChatCompletion));

        result.TotalCount.Should().Be(1);
        result.Items.Single().RequestId.Should().Be("req-a");
    }

    [Fact]
    public async Task QueryAsync_FiltersByDateRange()
    {
        var deploymentName = $"gpt-4o-{Guid.NewGuid():N}";
        var now = DateTimeOffset.UtcNow;
        await using var writeContext = fixture.CreateDbContext();
        var repository = new UsageRecordRepository(writeContext);
        await repository.AddAsync(UsageRecord.Create(
            Guid.NewGuid(), deploymentName, Guid.NewGuid(), "c", Guid.NewGuid(), ApiFamily.OpenAI,
            OperationType.ChatCompletion, "req-old", streaming: false, httpStatusCode: 200, durationMs: 1,
            totalTokens: 1, timestamp: now.AddDays(-10)));
        await repository.AddAsync(UsageRecord.Create(
            Guid.NewGuid(), deploymentName, Guid.NewGuid(), "c", Guid.NewGuid(), ApiFamily.OpenAI,
            OperationType.ChatCompletion, "req-recent", streaming: false, httpStatusCode: 200, durationMs: 1,
            totalTokens: 1, timestamp: now));

        await using var readContext = fixture.CreateDbContext();
        var result = await new UsageRecordRepository(readContext).QueryAsync(new UsageRecordQuery(
            ModelDeploymentName: deploymentName, From: now.AddDays(-1)));

        result.TotalCount.Should().Be(1);
        result.Items.Single().RequestId.Should().Be("req-recent");
    }

    [Fact]
    public async Task QueryAsync_PagesResultsNewestFirst()
    {
        var deploymentName = $"gpt-4o-{Guid.NewGuid():N}";
        var now = DateTimeOffset.UtcNow;
        await using var writeContext = fixture.CreateDbContext();
        var repository = new UsageRecordRepository(writeContext);
        for (var i = 0; i < 5; i++)
        {
            await repository.AddAsync(UsageRecord.Create(
                Guid.NewGuid(), deploymentName, Guid.NewGuid(), "c", Guid.NewGuid(), ApiFamily.OpenAI,
                OperationType.ChatCompletion, $"req-{i}", streaming: false, httpStatusCode: 200, durationMs: 1,
                totalTokens: 1, timestamp: now.AddMinutes(i)));
        }

        await using var readContext = fixture.CreateDbContext();
        var page1 = await new UsageRecordRepository(readContext).QueryAsync(new UsageRecordQuery(
            ModelDeploymentName: deploymentName, Page: 1, PageSize: 2));
        var page2 = await new UsageRecordRepository(readContext).QueryAsync(new UsageRecordQuery(
            ModelDeploymentName: deploymentName, Page: 2, PageSize: 2));

        page1.TotalCount.Should().Be(5);
        page1.Items.Select(i => i.RequestId).Should().Equal("req-4", "req-3");
        page2.Items.Select(i => i.RequestId).Should().Equal("req-2", "req-1");
    }
}
