using AiProxy.Application.Abstractions;
using AiProxy.Application.Exceptions;
using AiProxy.Application.Usage;
using AiProxy.Domain.Enums;
using FluentAssertions;
using NSubstitute;

namespace AiProxy.Application.Tests.Usage;

public class UsageExtractorResolverTests
{
    private static IUsageExtractor FakeExtractor(Provider provider, OperationType operationType)
    {
        var extractor = Substitute.For<IUsageExtractor>();
        extractor.Provider.Returns(provider);
        extractor.OperationType.Returns(operationType);
        return extractor;
    }

    [Fact]
    public void Resolve_WithMatchingExtractorRegistered_ReturnsIt()
    {
        var chatExtractor = FakeExtractor(Provider.OpenAI, OperationType.ChatCompletion);
        var imageExtractor = FakeExtractor(Provider.OpenAI, OperationType.ImageGeneration);
        var resolver = new UsageExtractorResolver([chatExtractor, imageExtractor]);

        var resolved = resolver.Resolve(Provider.OpenAI, OperationType.ImageGeneration);

        resolved.Should().Be(imageExtractor);
    }

    [Fact]
    public void Resolve_WithSameOperationTypeButDifferentApiFamily_PicksCorrectOne()
    {
        var openAiExtractor = FakeExtractor(Provider.OpenAI, OperationType.ChatCompletion);
        var anthropicExtractor = FakeExtractor(Provider.Anthropic, OperationType.ChatCompletion);
        var resolver = new UsageExtractorResolver([openAiExtractor, anthropicExtractor]);

        resolver.Resolve(Provider.Anthropic, OperationType.ChatCompletion).Should().Be(anthropicExtractor);
        resolver.Resolve(Provider.OpenAI, OperationType.ChatCompletion).Should().Be(openAiExtractor);
    }

    [Fact]
    public void Resolve_WithNoMatchingExtractor_ThrowsNotFoundException()
    {
        var resolver = new UsageExtractorResolver([FakeExtractor(Provider.OpenAI, OperationType.ChatCompletion)]);

        var act = () => resolver.Resolve(Provider.Anthropic, OperationType.ChatCompletion);

        act.Should().Throw<NotFoundException>();
    }
}
