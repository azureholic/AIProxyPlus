using System.Collections.Concurrent;
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
    : IEndpointCredentialCache
{
    private readonly ConcurrentDictionary<string, long> _cacheGenerations = new(StringComparer.Ordinal);
    private readonly TimeSpan _cacheTtl = cacheTtl ?? TimeSpan.FromMinutes(5);

    public async Task<EndpointCredential> GetCredentialAsync(ModelEndpoint assignment, CancellationToken cancellationToken = default)
    {
        var endpoint = assignment.Endpoint;
        return BuildCredential(assignment.Provider, await ResolveAsync(endpoint.GetActiveSecretReference(), endpoint.Id, cancellationToken));
    }

    public async Task<EndpointCredentialCandidates> GetCredentialCandidatesAsync(
        ModelEndpoint assignment,
        CancellationToken cancellationToken = default)
    {
        var endpoint = assignment.Endpoint;
        var activeRef = endpoint.GetActiveSecretReference();
        var active = BuildCredential(assignment.Provider, await ResolveAsync(activeRef, endpoint.Id, cancellationToken));
        var fallbackRef = endpoint.GetEligibleFallbackSecretReference(DateTimeOffset.UtcNow);
        Func<CancellationToken, Task<EndpointCredential>>? resolveFallback = fallbackRef is null
            ? null
            : async token => BuildCredential(
                assignment.Provider,
                await ResolveAsync(fallbackRef, endpoint.Id, token));
        return new EndpointCredentialCandidates(active, resolveFallback, endpoint.ActiveCredentialSlot, endpoint.FallbackCredentialSlot);
    }

    private async Task<string> ResolveAsync(string secretRef, Guid endpointId, CancellationToken cancellationToken)
    {
        var cacheKey = CacheKey(secretRef);
        if (!cache.TryGetValue(cacheKey, out string? apiKey))
        {
            var generation = _cacheGenerations.GetOrAdd(secretRef, 0);
            apiKey = await secretResolver.TryResolveAsync(secretRef, cancellationToken)
                ?? throw new InvalidOperationException($"No secret found at '{secretRef}' for endpoint '{endpointId}'.");
            if (_cacheGenerations.GetOrAdd(secretRef, 0) == generation)
            {
                cache.Set(cacheKey, apiKey, _cacheTtl);
            }
        }
        return apiKey!;
    }

    public void Invalidate(string secretRef)
    {
        _cacheGenerations.AddOrUpdate(secretRef, 1, static (_, generation) => generation + 1);
        cache.Remove(CacheKey(secretRef));
    }

    private static string CacheKey(string secretRef) => $"endpoint-secret:{secretRef}";

    private static EndpointCredential BuildCredential(Provider provider, string apiKey) => provider switch
    {
        Provider.MicrosoftAI => new EndpointCredential("api-key", apiKey),
        Provider.Anthropic => new EndpointCredential("x-api-key", apiKey),
        _ => new EndpointCredential("Authorization", $"Bearer {apiKey}"),
    };
}
