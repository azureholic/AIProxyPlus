using AiProxy.Application.Abstractions;
using AiProxy.Domain.Entities;
using AiProxy.Domain.Enums;
using Microsoft.Extensions.Caching.Memory;

namespace AiProxy.Infrastructure.Secrets;

/// <summary>
/// Resolves the outbound header for an <see cref="AuthMode.ApiKey"/> endpoint, caching the resolved secret
/// in-memory for a short TTL to avoid a Vault round trip per request (architecture.md §8).
/// </summary>
public sealed class ApiKeyCredentialProvider(ISecretResolver secretResolver, IMemoryCache cache, TimeSpan? cacheTtl = null)
{
    private readonly TimeSpan _cacheTtl = cacheTtl ?? TimeSpan.FromMinutes(5);

    public async Task<EndpointCredential> GetCredentialAsync(ModelEndpoint assignment, CancellationToken cancellationToken = default)
    {
        var endpoint = assignment.Endpoint;
        if (endpoint.SecretRef is null)
        {
            throw new InvalidOperationException($"Endpoint '{endpoint.Id}' has AuthMode.ApiKey but no SecretRef.");
        }

        var cacheKey = $"endpoint-secret:{endpoint.SecretRef}";
        if (!cache.TryGetValue(cacheKey, out string? apiKey))
        {
            apiKey = await secretResolver.TryResolveAsync(endpoint.SecretRef, cancellationToken)
                ?? throw new InvalidOperationException($"No secret found at '{endpoint.SecretRef}' for endpoint '{endpoint.Id}'.");
            cache.Set(cacheKey, apiKey, _cacheTtl);
        }

        return BuildCredential(assignment.Provider, apiKey!);
    }

    private static EndpointCredential BuildCredential(Provider provider, string apiKey) => provider switch
    {
        Provider.MicrosoftAI => new EndpointCredential("api-key", apiKey),
        Provider.Anthropic => new EndpointCredential("x-api-key", apiKey),
        _ => new EndpointCredential("Authorization", $"Bearer {apiKey}"),
    };
}
