using AiProxy.Application.Abstractions;
using AiProxy.Domain.Enums;
using AiProxy.Infrastructure.Usage;
using FluentAssertions;

namespace AiProxy.Infrastructure.Tests.Usage;

public class ImageUsageExtractorTests
{
    [Fact]
    public void ExtractFromResponseBody_WithDataArray_ReturnsImageCountAndSize()
    {
        var extractor = new ImageUsageExtractor(Provider.OpenAI, OperationType.ImageGeneration);

        var result = extractor.ExtractFromResponseBody(
            """{"data":[{"url":"https://example/1.png","size":"1024x1024"},{"url":"https://example/2.png"}]}""");

        result.Should().NotBeNull();
        result!.UsageMetrics.Should().NotBeNull();
        result.UsageMetrics!["image_count"].Should().Be("2");
        result.UsageMetrics!["size"].Should().Be("1024x1024");
        result.PromptTokens.Should().BeNull();
    }

    [Fact]
    public void ExtractFromResponseBody_WithTopLevelSizeAndQuality_IncludesBoth()
    {
        var extractor = new ImageUsageExtractor(Provider.OpenAI, OperationType.ImageEdit);

        var result = extractor.ExtractFromResponseBody("""{"data":[{"url":"x"}],"size":"512x512","quality":"hd"}""");

        result.Should().NotBeNull();
        result!.UsageMetrics!["quality"].Should().Be("hd");
        result.UsageMetrics!["size"].Should().Be("512x512");
    }

    [Fact]
    public void ExtractFromResponseBody_WithNoRecognizableFields_ReturnsNull()
    {
        var extractor = new ImageUsageExtractor(Provider.OpenAI, OperationType.ImageVariation);

        extractor.ExtractFromResponseBody("""{"id":"unrelated"}""").Should().BeNull();
    }

    [Fact]
    public void ExtractFromResponseBody_WithInvalidJson_ReturnsNull() =>
        new ImageUsageExtractor(Provider.OpenAI, OperationType.ImageGeneration).ExtractFromResponseBody("not json").Should().BeNull();

    [Fact]
    public void ExtractFromResponseBody_WithUsageObject_ReturnsTokensAlongsideMetrics()
    {
        var extractor = new ImageUsageExtractor(Provider.OpenAI, OperationType.ImageGeneration);

        var result = extractor.ExtractFromResponseBody(
            """
            {"data":[{"b64_json":"..."}],"size":"1024x1024","quality":"high",
             "usage":{"input_tokens":50,"output_tokens":50,"total_tokens":100}}
            """);

        result.Should().NotBeNull();
        result!.PromptTokens.Should().Be(50);
        result.CompletionTokens.Should().Be(50);
        result.TotalTokens.Should().Be(100);
        result.UsageMetrics!["size"].Should().Be("1024x1024");
        result.UsageMetrics!["quality"].Should().Be("high");
    }

    [Fact]
    public void ExtractFromResponseBody_WithMicrosoftAiUsageShape_MapsToStandardTokenFields()
    {
        var extractor = new ImageUsageExtractor(Provider.MicrosoftAI, OperationType.ImageGeneration);

        var result = extractor.ExtractFromResponseBody(
            """
            {"created":1,"data":[{"b64_json":"..."}],"model":"MAI-Image-2.5-Flash",
             "usage":{"num_output_tokens":1024,"num_input_text_tokens":10,"num_input_image_tokens":0}}
            """);

        result.Should().NotBeNull();
        result!.PromptTokens.Should().Be(10);
        result.CompletionTokens.Should().Be(1024);
        result.TotalTokens.Should().Be(1034);
    }

    [Fact]
    public void ExtractFromResponseBody_WithoutUsageObject_LeavesTokensNull()
    {
        var extractor = new ImageUsageExtractor(Provider.OpenAI, OperationType.ImageGeneration);

        var result = extractor.ExtractFromResponseBody("""{"data":[{"url":"https://example/1.png"}]}""");

        result.Should().NotBeNull();
        result!.PromptTokens.Should().BeNull();
        result.CompletionTokens.Should().BeNull();
        result.TotalTokens.Should().BeNull();
    }

    [Theory]
    [InlineData("image_generation.completed")]
    [InlineData("image_edit.completed")]
    public void TryExtractFromStreamingEvent_WithCompletedEvent_ExtractsTokenUsage(string eventType)
    {
        var extractor = new ImageUsageExtractor(Provider.OpenAI, OperationType.ImageGeneration);
        const string template =
            """{"type":"__TYPE__","b64_json":"...","size":"1024x1024","usage":{"input_tokens":50,"output_tokens":50,"total_tokens":100}}""";

        var result = extractor.TryExtractFromStreamingEvent(template.Replace("__TYPE__", eventType));

        result.Should().NotBeNull();
        result!.PromptTokens.Should().Be(50);
        result.CompletionTokens.Should().Be(50);
        result.TotalTokens.Should().Be(100);
        result.UsageMetrics!["size"].Should().Be("1024x1024");
    }

    [Fact]
    public void TryExtractFromStreamingEvent_WithPartialImageEvent_ReturnsNull()
    {
        var extractor = new ImageUsageExtractor(Provider.OpenAI, OperationType.ImageGeneration);

        extractor.TryExtractFromStreamingEvent(
                """{"type":"image_generation.partial_image","b64_json":"...","partial_image_index":0}""")
            .Should().BeNull();
    }

    [Fact]
    public void TryExtractFromStreamingEvent_WithUnrelatedEvent_ReturnsNull() =>
        new ImageUsageExtractor(Provider.OpenAI, OperationType.ImageGeneration).TryExtractFromStreamingEvent("{}").Should().BeNull();

    [Fact]
    public void IsUsageOnlyStreamingEvent_ReturnsFalse_SoTheFinalImageFrameIsNeverSuppressed()
    {
        IUsageExtractor extractor = new ImageUsageExtractor(Provider.OpenAI, OperationType.ImageGeneration);

        extractor.IsUsageOnlyStreamingEvent(
                """{"type":"image_generation.completed","b64_json":"...","usage":{"total_tokens":100}}""")
            .Should().BeFalse();
    }

    [Theory]
    [InlineData(OperationType.ImageGeneration)]
    [InlineData(OperationType.ImageEdit)]
    [InlineData(OperationType.ImageVariation)]
    public void OperationType_ReflectsConstructorArgument(OperationType operationType) =>
        new ImageUsageExtractor(Provider.OpenAI, operationType).OperationType.Should().Be(operationType);

    [Fact]
    public void Provider_ReflectsConstructorArgument() =>
        new ImageUsageExtractor(Provider.MicrosoftAI, OperationType.ImageGeneration).Provider.Should().Be(Provider.MicrosoftAI);
}
