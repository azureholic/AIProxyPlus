using AIProxyPlus.Domain.Entities;

namespace AIProxyPlus.Application.Abstractions;

/// <summary>An HTTP header (name + value) the forwarder attaches to prove the proxy's identity to an <see cref="Endpoint"/>.</summary>
public sealed record EndpointCredential(string HeaderName, string HeaderValue);
public sealed record EndpointCredentialCandidates(
    EndpointCredential Active,
    Func<CancellationToken, Task<EndpointCredential>>? ResolveFallbackAsync,
    Domain.Enums.EndpointCredentialSlot? ActiveSlot = null,
    Domain.Enums.EndpointCredentialSlot? FallbackSlot = null);

/// <summary>
/// Resolves the outbound credential for an endpoint, per its <see cref="Domain.Enums.AuthMode"/> — an API key
/// fetched via <see cref="ISecretResolver"/>, or a Managed Identity token (architecture.md §8).
/// </summary>
public interface ICredentialProvider
{
    Task<EndpointCredential> GetCredentialAsync(ModelEndpoint assignment, CancellationToken cancellationToken = default);

    async Task<EndpointCredentialCandidates> GetCredentialCandidatesAsync(
        ModelEndpoint assignment,
        CancellationToken cancellationToken = default) =>
        new(await GetCredentialAsync(assignment, cancellationToken), null);
}
