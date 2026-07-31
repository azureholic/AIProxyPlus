using AiProxy.Application.Abstractions;
using AiProxy.Application.Usage;
using AiProxy.Domain.Entities;
using AiProxy.Domain.Enums;
using FluentAssertions;
using FluentValidation;
using NSubstitute;

namespace AiProxy.Application.Tests.Usage;

public class QueryUsageRecordsTests
{
    private readonly IUsageRecordRepository _repository = Substitute.For<IUsageRecordRepository>();
    private readonly QueryUsageRecordsHandler _handler;

    public QueryUsageRecordsTests()
    {
        _handler = new QueryUsageRecordsHandler(_repository, new UsageRecordQueryValidator());
    }

    [Fact]
    public async Task HandleAsync_WithValidQuery_DelegatesToRepository()
    {
        var usageRecord = UsageRecord.Create(
            Guid.NewGuid(), "gpt-4o-prod", Guid.NewGuid(), "team-checkout", Guid.NewGuid(),
            ApiFamily.OpenAI, OperationType.ChatCompletion, "req-1", streaming: false, httpStatusCode: 200,
            durationMs: 100, totalTokens: 30);
        var expected = new PagedResult<UsageRecord>([usageRecord], 1, 1, 50);
        var query = new UsageRecordQuery(ModelDeploymentName: "gpt-4o-prod");
        _repository.QueryAsync(query, Arg.Any<CancellationToken>()).Returns(expected);

        var result = await _handler.HandleAsync(query);

        result.Should().Be(expected);
    }

    [Fact]
    public async Task HandleAsync_WithPageLessThanOne_ThrowsValidationException()
    {
        var act = () => _handler.HandleAsync(new UsageRecordQuery(Page: 0));

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task HandleAsync_WithPageSizeTooLarge_ThrowsValidationException()
    {
        var act = () => _handler.HandleAsync(new UsageRecordQuery(PageSize: 501));

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task HandleAsync_WithFromAfterTo_ThrowsValidationException()
    {
        var now = DateTimeOffset.UtcNow;
        var act = () => _handler.HandleAsync(new UsageRecordQuery(From: now, To: now.AddDays(-1)));

        await act.Should().ThrowAsync<ValidationException>();
    }
}
