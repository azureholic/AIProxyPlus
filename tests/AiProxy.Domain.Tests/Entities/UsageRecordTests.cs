using AiProxy.Domain.Entities;
using AiProxy.Domain.Enums;
using AiProxy.Domain.Exceptions;
using FluentAssertions;

namespace AiProxy.Domain.Tests.Entities;

public class UsageRecordTests
{
    [Fact]
    public void Create_WithTokensOnly_Succeeds()
    {
        var record = UsageRecord.Create(
            Guid.NewGuid(), "gpt-4o-prod", Guid.NewGuid(), "team-checkout", Guid.NewGuid(),
            Provider.OpenAI, OperationType.ChatCompletion, "req-1", streaming: false, httpStatusCode: 200,
            durationMs: 120, promptTokens: 10, completionTokens: 20, totalTokens: 30);

        record.TotalTokens.Should().Be(30);
        record.UsageMetrics.Should().BeNull();
    }

    [Fact]
    public void Create_WithUsageMetricsOnly_Succeeds()
    {
        var metrics = new Dictionary<string, string> { ["image_count"] = "2", ["image_size"] = "1024x1024" };

        var record = UsageRecord.Create(
            Guid.NewGuid(), "dall-e-prod", Guid.NewGuid(), "team-checkout", Guid.NewGuid(),
            Provider.OpenAI, OperationType.ImageGeneration, "req-2", streaming: false, httpStatusCode: 200,
            durationMs: 900, usageMetrics: metrics);

        record.PromptTokens.Should().BeNull();
        record.UsageMetrics.Should().BeEquivalentTo(metrics);
    }

    [Fact]
    public void Create_WithNeitherTokensNorMetrics_Throws()
    {
        var act = () => UsageRecord.Create(
            Guid.NewGuid(), "gpt-4o-prod", Guid.NewGuid(), "team-checkout", Guid.NewGuid(),
            Provider.OpenAI, OperationType.ChatCompletion, "req-3", streaming: false, httpStatusCode: 200,
            durationMs: 120);

        act.Should().Throw<DomainException>().WithMessage("*usage signal*");
    }

    [Fact]
    public void Create_WithEmptyConsumerId_Throws()
    {
        var act = () => UsageRecord.Create(
            Guid.NewGuid(), "gpt-4o-prod", Guid.Empty, "team-checkout", Guid.NewGuid(),
            Provider.OpenAI, OperationType.ChatCompletion, "req-4", streaming: false, httpStatusCode: 200,
            durationMs: 120, totalTokens: 10);

        act.Should().Throw<DomainException>().WithMessage("*ConsumerId*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithEmptyModelName_Throws(string name)
    {
        var act = () => UsageRecord.Create(
            Guid.NewGuid(), name, Guid.NewGuid(), "team-checkout", Guid.NewGuid(),
            Provider.OpenAI, OperationType.ChatCompletion, "req-5", streaming: false, httpStatusCode: 200,
            durationMs: 120, totalTokens: 10);

        act.Should().Throw<DomainException>().WithMessage("*ModelName*");
    }

    [Fact]
    public void Create_WithEmptyEndpointId_Throws()
    {
        var act = () => UsageRecord.Create(
            Guid.NewGuid(), "gpt-4o-prod", Guid.NewGuid(), "team-checkout", Guid.Empty,
            Provider.OpenAI, OperationType.ChatCompletion, "req-6", streaming: false, httpStatusCode: 200,
            durationMs: 120, totalTokens: 10);

        act.Should().Throw<DomainException>().WithMessage("*EndpointId*");
    }
}
