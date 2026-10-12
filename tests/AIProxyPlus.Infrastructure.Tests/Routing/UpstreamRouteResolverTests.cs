using AIProxyPlus.Domain.Entities;
using AIProxyPlus.Domain.Enums;
using AIProxyPlus.Infrastructure.Routing;
using FluentAssertions;

namespace AIProxyPlus.Infrastructure.Tests.Routing;

public class UpstreamRouteResolverTests
{
    private readonly UpstreamRouteResolver _resolver = new();

    [Theory]
    [InlineData(Provider.OpenAI, "/openai/v1", "https://foundry.example.com/openai/v1/images/generations?api-version=preview")]
    [InlineData(Provider.MicrosoftAI, "/mai/v1", "https://foundry.example.com/mai/v1/images/generations?api-version=preview")]
    public void ResolveHttp_ForFoundryImageRoute_ComposesProviderSpecificPrefix(
        Provider provider,
        string pathPrefix,
        string expected)
    {
        var assignment = CreateAssignment(provider, pathPrefix);

        var result = _resolver.ResolveHttp(assignment, OperationType.ImageGeneration, "?api-version=preview");

        result.Should().Be(new Uri(expected));
    }

    [Fact]
    public void ResolveHttp_ForAnthropicChat_UsesMessagesPath()
    {
        var assignment = CreateAssignment(Provider.Anthropic, "/anthropic/v1");

        var result = _resolver.ResolveHttp(assignment, OperationType.MessagesApi, string.Empty);

        result.Should().Be(new Uri("https://foundry.example.com/anthropic/v1/messages"));
    }

    [Theory]
    [InlineData(OperationType.SpeechToText, "/audio/transcriptions")]
    [InlineData(OperationType.TextToSpeech, "/audio/speech")]
    public void ResolveHttp_ForSpeechOperation_UsesAudioPath(OperationType operationType, string expectedSuffix)
    {
        var assignment = CreateAssignment(Provider.OpenAI, "/v1");

        var result = _resolver.ResolveHttp(assignment, operationType, string.Empty);

        result.Should().Be(new Uri($"https://foundry.example.com/v1{expectedSuffix}"));
    }

    [Fact]
    public void ResolveRealtime_ForOpenAi_UsesWebSocketSchemeAndPreservesQuery()
    {
        var assignment = CreateAssignment(Provider.OpenAI, "/v1");

        var result = _resolver.ResolveRealtime(assignment, "?model=gpt-realtime");

        result.Should().Be(new Uri("wss://foundry.example.com/v1/realtime?model=gpt-realtime"));
    }

    [Fact]
    public void ResolveRealtime_StripsProxyAuthenticationQueryParameters()
    {
        var assignment = CreateAssignment(Provider.OpenAI, "/v1");

        var result = _resolver.ResolveRealtime(assignment, "?model=gpt-realtime&api_key=aip_consumer_secret&ticket=one-time");

        result.Should().Be(new Uri("wss://foundry.example.com/v1/realtime?model=gpt-realtime"));
    }

    [Theory]
    [InlineData("?api_key=aip_consumer_secret", "")]
    [InlineData("?API_KEY=aip_consumer_secret&api-version=preview", "?api-version=preview")]
    [InlineData("?api%5Fkey=aip_consumer_secret&api-version=preview", "?api-version=preview")]
    [InlineData("?ticket=one-time&api-version=preview&api_key", "?api-version=preview")]
    [InlineData("?api-key=provider-param&api-version=preview", "?api-key=provider-param&api-version=preview")]
    public void ResolveHttp_StripsOnlyProxyAuthenticationQueryParameters(string query, string expectedQuery)
    {
        var assignment = CreateAssignment(Provider.OpenAI, "/v1");

        var result = _resolver.ResolveHttp(assignment, OperationType.ChatCompletion, query);

        result.Should().Be(new Uri($"https://foundry.example.com/v1/chat/completions{expectedQuery}"));
    }

    [Fact]
    public void ResolveHttp_ForUnsupportedProviderOperation_ThrowsBeforeComposition()
    {
        var assignment = CreateAssignment(Provider.Anthropic, "/anthropic/v1");

        var act = () => _resolver.ResolveHttp(assignment, OperationType.Embedding, string.Empty);

        act.Should().Throw<NotSupportedException>().WithMessage("*Anthropic*Embedding*");
    }

    private static ModelEndpoint CreateAssignment(Provider provider, string pathPrefix)
    {
        var endpoint = Endpoint.Create(
            Guid.NewGuid(), "Foundry", "https://foundry.example.com/", AuthMode.ApiKey, "endpoints/key");
        var route = ApiRoute.Create(Guid.NewGuid(), $"{provider} route", provider, pathPrefix);
        return ModelEndpoint.Create(Guid.NewGuid(), Guid.NewGuid(), endpoint, route, 1);
    }
}