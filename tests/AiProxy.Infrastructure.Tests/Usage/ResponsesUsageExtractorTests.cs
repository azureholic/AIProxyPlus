using AiProxy.Domain.Enums;
using AiProxy.Infrastructure.Usage;
using FluentAssertions;

namespace AiProxy.Infrastructure.Tests.Usage;

public class ResponsesUsageExtractorTests
{
    private readonly ResponsesUsageExtractor _extractor = new(ApiFamily.OpenAI);

    [Fact]
    public void ExtractFromResponseBody_WithUsage_ReturnsTokenCounts()
    {
        var result = _extractor.ExtractFromResponseBody(
            """{"id":"resp_1","usage":{"input_tokens":10,"output_tokens":4,"total_tokens":14}}""");

        result.Should().NotBeNull();
        result!.PromptTokens.Should().Be(10);
        result.CompletionTokens.Should().Be(4);
        result.TotalTokens.Should().Be(14);
    }

    [Fact]
    public void ExtractFromResponseBody_WithoutUsage_ReturnsNull() =>
        _extractor.ExtractFromResponseBody("""{"id":"resp_1"}""").Should().BeNull();

    [Fact]
    public void ExtractFromResponseBody_WithInvalidJson_ReturnsNull() =>
        _extractor.ExtractFromResponseBody("not json").Should().BeNull();

    [Fact]
    public void TryExtractFromStreamingEvent_ResponseCompleted_ReturnsTokenCounts()
    {
        var result = _extractor.TryExtractFromStreamingEvent(
            """{"type":"response.completed","response":{"id":"resp_1","usage":{"input_tokens":10,"output_tokens":4,"total_tokens":14}}}""");

        result.Should().NotBeNull();
        result!.TotalTokens.Should().Be(14);
    }

    [Fact]
    public void TryExtractFromStreamingEvent_OtherEventType_ReturnsNull() =>
        _extractor.TryExtractFromStreamingEvent("""{"type":"response.output_text.delta","delta":"hi"}""").Should().BeNull();

    [Fact]
    public void TryExtractFromStreamingEvent_ResponseCompletedWithoutUsage_ReturnsNull() =>
        _extractor.TryExtractFromStreamingEvent("""{"type":"response.completed","response":{"id":"resp_1"}}""").Should().BeNull();

    [Fact]
    public void TryExtractFromStreamingEvent_WithInvalidJson_ReturnsNull() =>
        _extractor.TryExtractFromStreamingEvent("not json").Should().BeNull();

    [Fact]
    public void OperationType_IsAlwaysResponsesApi() => _extractor.OperationType.Should().Be(OperationType.ResponsesApi);

    [Theory]
    [InlineData(ApiFamily.OpenAI)]
    [InlineData(ApiFamily.AzureOpenAI)]
    [InlineData(ApiFamily.OpenAICompatible)]
    public void ApiFamily_ReflectsConstructorArgument(ApiFamily apiFamily) => new ResponsesUsageExtractor(apiFamily).ApiFamily.Should().Be(apiFamily);
}
