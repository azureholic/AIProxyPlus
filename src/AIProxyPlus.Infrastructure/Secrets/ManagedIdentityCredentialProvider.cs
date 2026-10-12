using Azure.Core;
using AIProxyPlus.Application.Abstractions;
using AIProxyPlus.Domain.Entities;

namespace AIProxyPlus.Infrastructure.Secrets;

/// <summary>
/// Resolves a bearer token for an <see cref="Domain.Enums.AuthMode.ManagedIdentity"/> endpoint via
/// <see cref="TokenCredential"/> (typically <c>DefaultAzureCredential</c>) — the only place this solution
/// touches Azure, and only when an admin opts an endpoint into it (architecture.md §8).
/// </summary>
public sealed class ManagedIdentityCredentialProvider(TokenCredential tokenCredential)
{
    public async Task<EndpointCredential> GetCredentialAsync(ModelEndpoint assignment, CancellationToken cancellationToken = default)
    {
        var endpoint = assignment.Endpoint;
        if (string.IsNullOrWhiteSpace(endpoint.ManagedIdentityScope))
        {
            throw new InvalidOperationException($"Endpoint '{endpoint.Id}' has AuthMode.ManagedIdentity but no ManagedIdentityScope.");
        }

        var token = await tokenCredential.GetTokenAsync(
            new TokenRequestContext(
                [endpoint.ManagedIdentityScope],
                tenantId: endpoint.ManagedIdentityTenantId),
            cancellationToken);

        return new EndpointCredential("Authorization", $"Bearer {token.Token}");
    }
}
