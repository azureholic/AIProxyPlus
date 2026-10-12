namespace AIProxyPlus.Application.Abstractions;

/// <summary>Generates a new, cryptographically random plaintext API key for a <see cref="Domain.Entities.Consumer"/>.</summary>
public interface IApiKeyGenerator
{
    string Generate();
}
