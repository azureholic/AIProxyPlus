using AIProxyPlus.Application.Abstractions;
using AIProxyPlus.Application.Exceptions;
using AIProxyPlus.Application.Usage;
using AIProxyPlus.Domain.Enums;
using FluentAssertions;
using NSubstitute;

namespace AIProxyPlus.Application.Tests.Usage;

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
        var microsoftAiExtractor = FakeExtractor(Provider.MicrosoftAI, OperationType.ChatCompletion);
        var resolver = new UsageExtractorResolver([openAiExtractor, microsoftAiExtractor]);

        resolver.Resolve(Provider.MicrosoftAI, OperationType.ChatCompletion).Should().Be(microsoftAiExtractor);
        resolver.Resolve(Provider.OpenAI, OperationType.ChatCompletion).Should().Be(openAiExtractor);
    }

    [Fact]
    public void Resolve_ForAnthropicMessagesApi_ReturnsRegisteredExtractor()
    {
        var openAiExtractor = FakeExtractor(Provider.OpenAI, OperationType.ChatCompletion);
        var anthropicExtractor = FakeExtractor(Provider.Anthropic, OperationType.MessagesApi);
        var resolver = new UsageExtractorResolver([openAiExtractor, anthropicExtractor]);

        resolver.Resolve(Provider.Anthropic, OperationType.MessagesApi).Should().Be(anthropicExtractor);
    }

    [Fact]
    public void Resolve_WithNoMatchingExtractor_ThrowsNotFoundException()
    {
        var resolver = new UsageExtractorResolver([FakeExtractor(Provider.OpenAI, OperationType.ChatCompletion)]);

        var act = () => resolver.Resolve(Provider.Anthropic, OperationType.ChatCompletion);

        act.Should().Throw<NotFoundException>();
    }
}
