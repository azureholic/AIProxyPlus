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
        var modelName = $"gpt-4o-{Guid.NewGuid():N}";
        var consumerId = Guid.NewGuid();
        await using var writeContext = fixture.CreateDbContext();
        var record = UsageRecord.Create(
            Guid.NewGuid(), modelName, consumerId, "team-checkout", Guid.NewGuid(),
            Provider.OpenAI, OperationType.ChatCompletion, "req-1", streaming: false, httpStatusCode: 200,
            durationMs: 120, promptTokens: 10, completionTokens: 20, totalTokens: 30);
        await new UsageRecordRepository(writeContext).AddAsync(record);

        await using var readContext = fixture.CreateDbContext();
        var result = await new UsageRecordRepository(readContext).QueryAsync(new UsageRecordQuery(ModelName: modelName));

        result.TotalCount.Should().Be(1);
        result.Items.Single().TotalTokens.Should().Be(30);
        result.Items.Single().UsageMetrics.Should().BeNull();
    }

    [Fact]
    public async Task AddAsync_Then_Query_RoundTripsUsageMetricsDictionary()
    {
        var modelName = $"dall-e-{Guid.NewGuid():N}";
        var metrics = new Dictionary<string, string> { ["image_count"] = "2", ["image_size"] = "1024x1024" };
        await using var writeContext = fixture.CreateDbContext();
        var record = UsageRecord.Create(
            Guid.NewGuid(), modelName, Guid.NewGuid(), "team-checkout", Guid.NewGuid(),
            Provider.OpenAI, OperationType.ImageGeneration, "req-2", streaming: false, httpStatusCode: 200,
            durationMs: 900, usageMetrics: metrics);
        await new UsageRecordRepository(writeContext).AddAsync(record);

        await using var readContext = fixture.CreateDbContext();
        var result = await new UsageRecordRepository(readContext).QueryAsync(new UsageRecordQuery(ModelName: modelName));

        result.Items.Single().PromptTokens.Should().BeNull();
        result.Items.Single().UsageMetrics.Should().BeEquivalentTo(metrics);
    }

    [Fact]
    public async Task QueryAsync_FiltersByConsumerNameAndOperationType()
    {
        var modelName = $"gpt-4o-{Guid.NewGuid():N}";
        var consumerName = $"team-{Guid.NewGuid():N}";
        await using var writeContext = fixture.CreateDbContext();
        var repository = new UsageRecordRepository(writeContext);
        await repository.AddAsync(UsageRecord.Create(
            Guid.NewGuid(), modelName, Guid.NewGuid(), consumerName, Guid.NewGuid(),
            Provider.OpenAI, OperationType.ChatCompletion, "req-a", streaming: false, httpStatusCode: 200,
            durationMs: 100, totalTokens: 1));
        await repository.AddAsync(UsageRecord.Create(
            Guid.NewGuid(), modelName, Guid.NewGuid(), "someone-else", Guid.NewGuid(),
            Provider.OpenAI, OperationType.ChatCompletion, "req-b", streaming: false, httpStatusCode: 200,
            durationMs: 100, totalTokens: 1));
        await repository.AddAsync(UsageRecord.Create(
            Guid.NewGuid(), modelName, Guid.NewGuid(), consumerName, Guid.NewGuid(),
            Provider.OpenAI, OperationType.Embedding, "req-c", streaming: false, httpStatusCode: 200,
            durationMs: 100, totalTokens: 1));

        await using var readContext = fixture.CreateDbContext();
        var result = await new UsageRecordRepository(readContext).QueryAsync(new UsageRecordQuery(
            ModelName: modelName, ConsumerName: consumerName, OperationType: OperationType.ChatCompletion));

        result.TotalCount.Should().Be(1);
        result.Items.Single().RequestId.Should().Be("req-a");
    }

    [Fact]
    public async Task QueryAsync_FiltersByDateRange()
    {
        var modelName = $"gpt-4o-{Guid.NewGuid():N}";
        var now = DateTimeOffset.UtcNow;
        await using var writeContext = fixture.CreateDbContext();
        var repository = new UsageRecordRepository(writeContext);
        await repository.AddAsync(UsageRecord.Create(
            Guid.NewGuid(), modelName, Guid.NewGuid(), "c", Guid.NewGuid(), Provider.OpenAI,
            OperationType.ChatCompletion, "req-old", streaming: false, httpStatusCode: 200, durationMs: 1,
            totalTokens: 1, timestamp: now.AddDays(-10)));
        await repository.AddAsync(UsageRecord.Create(
            Guid.NewGuid(), modelName, Guid.NewGuid(), "c", Guid.NewGuid(), Provider.OpenAI,
            OperationType.ChatCompletion, "req-recent", streaming: false, httpStatusCode: 200, durationMs: 1,
            totalTokens: 1, timestamp: now));

        await using var readContext = fixture.CreateDbContext();
        var result = await new UsageRecordRepository(readContext).QueryAsync(new UsageRecordQuery(
            ModelName: modelName, From: now.AddDays(-1)));

        result.TotalCount.Should().Be(1);
        result.Items.Single().RequestId.Should().Be("req-recent");
    }

    [Fact]
    public async Task QueryAsync_PagesResultsNewestFirst()
    {
        var modelName = $"gpt-4o-{Guid.NewGuid():N}";
        var now = DateTimeOffset.UtcNow;
        await using var writeContext = fixture.CreateDbContext();
        var repository = new UsageRecordRepository(writeContext);
        for (var i = 0; i < 5; i++)
        {
            await repository.AddAsync(UsageRecord.Create(
                Guid.NewGuid(), modelName, Guid.NewGuid(), "c", Guid.NewGuid(), Provider.OpenAI,
                OperationType.ChatCompletion, $"req-{i}", streaming: false, httpStatusCode: 200, durationMs: 1,
                totalTokens: 1, timestamp: now.AddMinutes(i)));
        }

        await using var readContext = fixture.CreateDbContext();
        var page1 = await new UsageRecordRepository(readContext).QueryAsync(new UsageRecordQuery(
            ModelName: modelName, Page: 1, PageSize: 2));
        var page2 = await new UsageRecordRepository(readContext).QueryAsync(new UsageRecordQuery(
            ModelName: modelName, Page: 2, PageSize: 2));

        page1.TotalCount.Should().Be(5);
        page1.Items.Select(i => i.RequestId).Should().Equal("req-4", "req-3");
        page2.Items.Select(i => i.RequestId).Should().Equal("req-2", "req-1");
    }
}
