namespace AiProxy.Domain.Enums;

/// <summary>The upstream API shape an <see cref="Entities.Endpoint"/> speaks.</summary>
public enum ApiFamily
{
    OpenAI,
    AzureOpenAI,
    Anthropic,
    OpenAICompatible
}
