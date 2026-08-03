using AiProxy.Application.Abstractions;
using AiProxy.Domain.Enums;
using AiProxy.Infrastructure.Usage;
using FluentAssertions;

namespace AiProxy.Infrastructure.Tests.Usage;

public class AnthropicChatUsageExtractorTests
{
    private readonly AnthropicChatUsageExtractor _extractor = new();

    [Fact]
    public void ExtractFromResponseBody_WithUsage_ReturnsInputAndOutputTokens()
    {
        var result = _extractor.ExtractFromResponseBody("""{"id":"x","usage":{"input_tokens":12,"output_tokens":6}}""");

        result.Should().NotBeNull();
        result!.PromptTokens.Should().Be(12);
        result.CompletionTokens.Should().Be(6);
        result.TotalTokens.Should().Be(18);
    }

    [Fact]
    public void ExtractFromResponseBody_WithoutUsage_ReturnsNull() =>
        _extractor.ExtractFromResponseBody("""{"id":"x"}""").Should().BeNull();

    [Fact]
    public void ExtractFromResponseBody_WithInvalidJson_ReturnsNull() =>
        _extractor.ExtractFromResponseBody("not json").Should().BeNull();

    [Fact]
    public void TryExtractFromStreamingEvent_MessageStart_ReturnsInputTokens()
    {
        var result = _extractor.TryExtractFromStreamingEvent(
            """{"type":"message_start","message":{"id":"msg_1","usage":{"input_tokens":12,"output_tokens":0}}}""");

        result.Should().NotBeNull();
        result!.PromptTokens.Should().Be(12);
    }

    [Fact]
    public void TryExtractFromStreamingEvent_MessageDelta_ReturnsOutputTokens()
    {
        var result = _extractor.TryExtractFromStreamingEvent(
            """{"type":"message_delta","delta":{"stop_reason":"end_turn"},"usage":{"output_tokens":6}}""");

        result.Should().NotBeNull();
        result!.CompletionTokens.Should().Be(6);
    }

    [Fact]
    public void TryExtractFromStreamingEvent_ContentBlockDelta_ReturnsNull() =>
        _extractor.TryExtractFromStreamingEvent("""{"type":"content_block_delta","delta":{"text":"hi"}}""").Should().BeNull();

    [Fact]
    public void TryExtractFromStreamingEvent_WithInvalidJson_ReturnsNull() =>
        _extractor.TryExtractFromStreamingEvent("not json").Should().BeNull();

    [Fact]
    public void IsUsageOnlyStreamingEvent_AlwaysFalse_SinceAnthropicFramesAreContentBearing() =>
        ((IUsageExtractor)_extractor).IsUsageOnlyStreamingEvent("""{"type":"message_start","message":{"usage":{"input_tokens":1,"output_tokens":0}}}""")
            .Should().BeFalse();

    [Fact]
    public void ProviderAndOperationType_AreAnthropicChatCompletion()
    {
        _extractor.Provider.Should().Be(Provider.Anthropic);
        _extractor.OperationType.Should().Be(OperationType.ChatCompletion);
    }
}
