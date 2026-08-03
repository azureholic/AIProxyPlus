using AiProxy.Domain.Enums;
using AiProxy.Infrastructure.Usage;
using FluentAssertions;

namespace AiProxy.Infrastructure.Tests.Usage;

public class OpenAiStyleTokenUsageExtractorTests
{
    private readonly OpenAiStyleTokenUsageExtractor _extractor = new(Provider.OpenAI, OperationType.ChatCompletion);

    [Fact]
    public void ExtractFromResponseBody_WithUsage_ReturnsTokenCounts()
    {
        var result = _extractor.ExtractFromResponseBody("""{"id":"x","usage":{"prompt_tokens":10,"completion_tokens":5,"total_tokens":15}}""");

        result.Should().NotBeNull();
        result!.PromptTokens.Should().Be(10);
        result.CompletionTokens.Should().Be(5);
        result.TotalTokens.Should().Be(15);
    }

    [Fact]
    public void ExtractFromResponseBody_WithoutUsage_ReturnsNull() =>
        _extractor.ExtractFromResponseBody("""{"id":"x"}""").Should().BeNull();

    [Fact]
    public void ExtractFromResponseBody_WithInvalidJson_ReturnsNull() =>
        _extractor.ExtractFromResponseBody("not json").Should().BeNull();

    [Fact]
    public void TryExtractFromStreamingEvent_WithUsage_ReturnsTokenCounts()
    {
        var result = _extractor.TryExtractFromStreamingEvent(
            """{"id":"x","choices":[],"usage":{"prompt_tokens":7,"completion_tokens":2,"total_tokens":9}}""");

        result.Should().NotBeNull();
        result!.TotalTokens.Should().Be(9);
    }

    [Fact]
    public void IsUsageOnlyStreamingEvent_WithUsageAndEmptyChoices_ReturnsTrue() =>
        _extractor.IsUsageOnlyStreamingEvent("""{"choices":[],"usage":{"total_tokens":9}}""").Should().BeTrue();

    [Fact]
    public void IsUsageOnlyStreamingEvent_WithUsageAndNonEmptyChoices_ReturnsFalse() =>
        _extractor.IsUsageOnlyStreamingEvent("""{"choices":[{"delta":{"content":"hi"}}],"usage":{"total_tokens":9}}""").Should().BeFalse();

    [Fact]
    public void IsUsageOnlyStreamingEvent_WithoutUsage_ReturnsFalse() =>
        _extractor.IsUsageOnlyStreamingEvent("""{"choices":[{"delta":{"content":"hi"}}]}""").Should().BeFalse();

    [Fact]
    public void IsUsageOnlyStreamingEvent_WithInvalidJson_ReturnsFalse() =>
        _extractor.IsUsageOnlyStreamingEvent("[DONE]").Should().BeFalse();

    [Fact]
    public void ProviderAndOperationType_ReflectConstructorArguments()
    {
        _extractor.Provider.Should().Be(Provider.OpenAI);
        _extractor.OperationType.Should().Be(OperationType.ChatCompletion);
    }
}
