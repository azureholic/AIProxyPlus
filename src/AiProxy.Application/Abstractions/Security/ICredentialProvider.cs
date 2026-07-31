using AiProxy.Domain.Entities;

namespace AiProxy.Application.Abstractions;

/// <summary>An HTTP header (name + value) the forwarder attaches to prove the proxy's identity to an <see cref="Endpoint"/>.</summary>
public sealed record EndpointCredential(string HeaderName, string HeaderValue);

/// <summary>
/// Resolves the outbound credential for an endpoint, per its <see cref="Domain.Enums.AuthMode"/> — an API key
/// fetched via <see cref="ISecretResolver"/>, or a Managed Identity token (architecture.md §8).
/// </summary>
public interface ICredentialProvider
{
    Task<EndpointCredential> GetCredentialAsync(Endpoint endpoint, CancellationToken cancellationToken = default);
}
