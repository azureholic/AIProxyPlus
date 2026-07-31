using AiProxy.Application.Abstractions;
using AiProxy.Domain.Entities;
using AiProxy.Domain.Enums;

namespace AiProxy.Infrastructure.Secrets;

/// <summary>Dispatches to <see cref="ApiKeyCredentialProvider"/> or <see cref="ManagedIdentityCredentialProvider"/> per <see cref="Endpoint.AuthMode"/>.</summary>
public sealed class CredentialProvider(
    ApiKeyCredentialProvider apiKeyCredentialProvider,
    ManagedIdentityCredentialProvider managedIdentityCredentialProvider) : ICredentialProvider
{
    public Task<EndpointCredential> GetCredentialAsync(Endpoint endpoint, CancellationToken cancellationToken = default) =>
        endpoint.AuthMode switch
        {
            AuthMode.ApiKey => apiKeyCredentialProvider.GetCredentialAsync(endpoint, cancellationToken),
            AuthMode.ManagedIdentity => managedIdentityCredentialProvider.GetCredentialAsync(endpoint, cancellationToken),
            _ => throw new NotSupportedException($"Unsupported AuthMode '{endpoint.AuthMode}'."),
        };
}
