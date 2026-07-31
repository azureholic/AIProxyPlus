using AiProxy.Domain.Enums;
using AiProxy.Infrastructure.Usage;
using FluentAssertions;

namespace AiProxy.Infrastructure.Tests.Usage;

public class ImageUsageExtractorTests
{
    [Fact]
    public void ExtractFromResponseBody_WithDataArray_ReturnsImageCountAndSize()
    {
        var extractor = new ImageUsageExtractor(OperationType.ImageGeneration);

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
        var extractor = new ImageUsageExtractor(OperationType.ImageEdit);

        var result = extractor.ExtractFromResponseBody("""{"data":[{"url":"x"}],"size":"512x512","quality":"hd"}""");

        result.Should().NotBeNull();
        result!.UsageMetrics!["quality"].Should().Be("hd");
        result.UsageMetrics!["size"].Should().Be("512x512");
    }

    [Fact]
    public void ExtractFromResponseBody_WithNoRecognizableFields_ReturnsNull()
    {
        var extractor = new ImageUsageExtractor(OperationType.ImageVariation);

        extractor.ExtractFromResponseBody("""{"id":"unrelated"}""").Should().BeNull();
    }

    [Fact]
    public void ExtractFromResponseBody_WithInvalidJson_ReturnsNull() =>
        new ImageUsageExtractor(OperationType.ImageGeneration).ExtractFromResponseBody("not json").Should().BeNull();

    [Fact]
    public void TryExtractFromStreamingEvent_AlwaysNull_SinceImagesApiIsNeverStreamed() =>
        new ImageUsageExtractor(OperationType.ImageGeneration).TryExtractFromStreamingEvent("{}").Should().BeNull();

    [Theory]
    [InlineData(OperationType.ImageGeneration)]
    [InlineData(OperationType.ImageEdit)]
    [InlineData(OperationType.ImageVariation)]
    public void OperationType_ReflectsConstructorArgument(OperationType operationType) =>
        new ImageUsageExtractor(operationType).OperationType.Should().Be(operationType);

    [Fact]
    public void ApiFamily_IsAlwaysOpenAi() => new ImageUsageExtractor(OperationType.ImageGeneration).ApiFamily.Should().Be(ApiFamily.OpenAI);
}
