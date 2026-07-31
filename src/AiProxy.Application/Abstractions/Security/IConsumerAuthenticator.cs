using AiProxy.Domain.Entities;

namespace AiProxy.Application.Abstractions;

/// <summary>
/// Authenticates an inbound caller by their plaintext API key, returning the matching enabled
/// <see cref="Consumer"/> or <see langword="null"/> if it's missing, unknown, or revoked (architecture.md §13).
/// </summary>
public interface IConsumerAuthenticator
{
    Task<Consumer?> AuthenticateAsync(string plaintextApiKey, CancellationToken cancellationToken = default);
}
