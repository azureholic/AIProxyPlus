using AIProxyPlus.Application.Abstractions;
using AIProxyPlus.Domain.Entities;
using AIProxyPlus.Domain.Enums;

namespace AIProxyPlus.Infrastructure.Secrets;

/// <summary>Dispatches to <see cref="ApiKeyCredentialProvider"/> or <see cref="ManagedIdentityCredentialProvider"/> per <see cref="Endpoint.AuthMode"/>.</summary>
public sealed class CredentialProvider(
    ApiKeyCredentialProvider apiKeyCredentialProvider,
    ManagedIdentityCredentialProvider managedIdentityCredentialProvider) : ICredentialProvider
{
    public Task<EndpointCredential> GetCredentialAsync(ModelEndpoint assignment, CancellationToken cancellationToken = default) =>
        assignment.AuthMode switch
        {
            AuthMode.ApiKey => apiKeyCredentialProvider.GetCredentialAsync(assignment, cancellationToken),
            AuthMode.ManagedIdentity => managedIdentityCredentialProvider.GetCredentialAsync(assignment, cancellationToken),
            _ => throw new NotSupportedException($"Unsupported AuthMode '{assignment.AuthMode}'."),
        };

    public async Task<EndpointCredentialCandidates> GetCredentialCandidatesAsync(
        ModelEndpoint assignment,
        CancellationToken cancellationToken = default) =>
        assignment.AuthMode switch
        {
            AuthMode.ApiKey => await apiKeyCredentialProvider.GetCredentialCandidatesAsync(assignment, cancellationToken),
            AuthMode.ManagedIdentity => new(
                await managedIdentityCredentialProvider.GetCredentialAsync(assignment, cancellationToken), null),
            _ => throw new NotSupportedException($"Unsupported AuthMode '{assignment.AuthMode}'."),
        };
}
