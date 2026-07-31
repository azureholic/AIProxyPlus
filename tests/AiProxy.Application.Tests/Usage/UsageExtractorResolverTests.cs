using AiProxy.Application.Abstractions;
using AiProxy.Application.Exceptions;
using AiProxy.Application.Usage;
using AiProxy.Domain.Enums;
using FluentAssertions;
using NSubstitute;

namespace AiProxy.Application.Tests.Usage;

public class UsageExtractorResolverTests
{
    private static IUsageExtractor FakeExtractor(ApiFamily apiFamily, OperationType operationType)
    {
        var extractor = Substitute.For<IUsageExtractor>();
        extractor.ApiFamily.Returns(apiFamily);
        extractor.OperationType.Returns(operationType);
        return extractor;
    }

    [Fact]
    public void Resolve_WithMatchingExtractorRegistered_ReturnsIt()
    {
        var chatExtractor = FakeExtractor(ApiFamily.OpenAI, OperationType.ChatCompletion);
        var imageExtractor = FakeExtractor(ApiFamily.OpenAI, OperationType.ImageGeneration);
        var resolver = new UsageExtractorResolver([chatExtractor, imageExtractor]);

        var resolved = resolver.Resolve(ApiFamily.OpenAI, OperationType.ImageGeneration);

        resolved.Should().Be(imageExtractor);
    }

    [Fact]
    public void Resolve_WithSameOperationTypeButDifferentApiFamily_PicksCorrectOne()
    {
        var openAiExtractor = FakeExtractor(ApiFamily.OpenAI, OperationType.ChatCompletion);
        var anthropicExtractor = FakeExtractor(ApiFamily.Anthropic, OperationType.ChatCompletion);
        var resolver = new UsageExtractorResolver([openAiExtractor, anthropicExtractor]);

        resolver.Resolve(ApiFamily.Anthropic, OperationType.ChatCompletion).Should().Be(anthropicExtractor);
        resolver.Resolve(ApiFamily.OpenAI, OperationType.ChatCompletion).Should().Be(openAiExtractor);
    }

    [Fact]
    public void Resolve_WithNoMatchingExtractor_ThrowsNotFoundException()
    {
        var resolver = new UsageExtractorResolver([FakeExtractor(ApiFamily.OpenAI, OperationType.ChatCompletion)]);

        var act = () => resolver.Resolve(ApiFamily.Anthropic, OperationType.ChatCompletion);

        act.Should().Throw<NotFoundException>();
    }
}
