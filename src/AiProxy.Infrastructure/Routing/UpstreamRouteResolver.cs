using AiProxy.Domain.Entities;
using AiProxy.Domain.Enums;

namespace AiProxy.Infrastructure.Routing;

public sealed class UpstreamRouteResolver
{
    public Uri ResolveHttp(ModelEndpoint assignment, OperationType operationType, string queryString)
    {
        var suffix = ResolveSuffix(assignment.Provider, operationType);
        return Compose(assignment.BaseUrl, assignment.ApiRoute.PathPrefix, suffix, queryString, webSocket: false);
    }

    public Uri ResolveRealtime(ModelEndpoint assignment, string queryString)
    {
        var suffix = ResolveSuffix(assignment.Provider, OperationType.RealtimeVoice);
        return Compose(assignment.BaseUrl, assignment.ApiRoute.PathPrefix, suffix, queryString, webSocket: true);
    }

    private static string ResolveSuffix(Provider provider, OperationType operationType) => (provider, operationType) switch
    {
        (Provider.OpenAI or Provider.MicrosoftAI, OperationType.ChatCompletion) => "/chat/completions",
        (Provider.OpenAI or Provider.MicrosoftAI, OperationType.Completion) => "/completions",
        (Provider.OpenAI or Provider.MicrosoftAI, OperationType.Embedding) => "/embeddings",
        (Provider.OpenAI or Provider.MicrosoftAI, OperationType.ImageGeneration) => "/images/generations",
        (Provider.OpenAI or Provider.MicrosoftAI, OperationType.ImageEdit) => "/images/edits",
        (Provider.OpenAI or Provider.MicrosoftAI, OperationType.ImageVariation) => "/images/variations",
        (Provider.OpenAI or Provider.MicrosoftAI, OperationType.ResponsesApi) => "/responses",
        (Provider.OpenAI, OperationType.RealtimeVoice) => "/realtime",
        (Provider.Anthropic, OperationType.ChatCompletion) => "/messages",
        _ => throw new NotSupportedException($"Provider '{provider}' does not support operation '{operationType}'."),
    };

    private static Uri Compose(
        string baseUrl,
        string pathPrefix,
        string suffix,
        string queryString,
        bool webSocket)
    {
        var normalizedBaseUrl = baseUrl.TrimEnd('/');
        if (webSocket)
        {
            normalizedBaseUrl = normalizedBaseUrl
                .Replace("https://", "wss://", StringComparison.OrdinalIgnoreCase)
                .Replace("http://", "ws://", StringComparison.OrdinalIgnoreCase);
        }

        var prefix = pathPrefix == "/" ? string.Empty : pathPrefix.TrimEnd('/');
        var query = string.IsNullOrEmpty(queryString) ? string.Empty : queryString;
        return new Uri($"{normalizedBaseUrl}{prefix}/{suffix.TrimStart('/')}{query}");
    }
}