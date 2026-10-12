using AIProxyPlus.Domain.Entities;
using AIProxyPlus.Domain.Enums;
using AIProxyPlus.Infrastructure.Usage;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace AIProxyPlus.Infrastructure.Tests.Usage;

public class UsageRecordChannelTests
{
    private static UsageRecord NewUsageRecord(string requestId = "req-1") =>
        UsageRecord.Create(
            Guid.NewGuid(), "gpt-4o-prod", Guid.NewGuid(), "team-checkout", Guid.NewGuid(), "prod-endpoint",
            Provider.OpenAI, OperationType.ChatCompletion, requestId, streaming: false, httpStatusCode: 200,
            durationMs: 100, totalTokens: 10);

    [Fact]
    public void Enqueue_WithinCapacity_DoesNotLog()
    {
        var logger = Substitute.For<ILogger<UsageRecordChannel>>();
        var channel = new UsageRecordChannel(logger, capacity: 2);

        channel.Enqueue(NewUsageRecord());
        channel.Enqueue(NewUsageRecord());

        logger.DidNotReceiveWithAnyArgs().Log<object>(default, default, default!, default, default!);
    }

    [Fact]
    public void Enqueue_BeyondCapacity_DropsAndLogsError()
    {
        var logger = Substitute.For<ILogger<UsageRecordChannel>>();
        var channel = new UsageRecordChannel(logger, capacity: 1);

        channel.Enqueue(NewUsageRecord("req-fits"));
        channel.Enqueue(NewUsageRecord("req-dropped"));

        logger.Received(1).Log(
            LogLevel.Error,
            Arg.Any<EventId>(),
            Arg.Any<object>(),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Fact]
    public async Task Reader_YieldsEnqueuedRecordsInOrder()
    {
        var channel = new UsageRecordChannel(Substitute.For<ILogger<UsageRecordChannel>>(), capacity: 10);

        channel.Enqueue(NewUsageRecord("req-1"));
        channel.Enqueue(NewUsageRecord("req-2"));

        var first = await channel.Reader.ReadAsync();
        var second = await channel.Reader.ReadAsync();

        first.RequestId.Should().Be("req-1");
        second.RequestId.Should().Be("req-2");
    }
}
