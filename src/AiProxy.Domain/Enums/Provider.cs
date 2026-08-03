namespace AiProxy.Domain.Enums;

/// <summary>The upstream API provider whose protocol behavior an API route exposes.</summary>
public enum Provider
{
    OpenAI,
    MicrosoftAI,
    Anthropic
}