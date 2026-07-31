using AiProxy.Application.Abstractions;
using AiProxy.Domain.Entities;

namespace AiProxy.Application.Consumers;

/// <summary>
/// Default authenticator: hashes the candidate key (deterministic HMAC, per architecture.md §8) and looks up
/// the consumer by that exact hash — no need to iterate/verify every row.
/// </summary>
public sealed class ConsumerAuthenticator(IConsumerRepository repository, IApiKeyHasher hasher) : IConsumerAuthenticator
{
    public async Task<Consumer?> AuthenticateAsync(string plaintextApiKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(plaintextApiKey))
        {
            return null;
        }

        var hash = hasher.Hash(plaintextApiKey);
        var consumer = await repository.GetByApiKeyHashAsync(hash, cancellationToken);

        return consumer is { Enabled: true } ? consumer : null;
    }
}
