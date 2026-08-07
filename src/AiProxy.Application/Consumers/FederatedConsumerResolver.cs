using AiProxy.Application.Abstractions;
using AiProxy.Domain.Entities;

namespace AiProxy.Application.Consumers;

public sealed record OidcBearerIdentity(string Issuer, string Subject);

public sealed record ManagedIdentityBearerIdentity(Guid TenantId, Guid ObjectId);

public interface IFederatedConsumerResolver
{
    Task<Consumer?> ResolveOidcAsync(
        OidcBearerIdentity identity,
        CancellationToken cancellationToken = default);

    Task<Consumer?> ResolveManagedIdentityAsync(
        ManagedIdentityBearerIdentity identity,
        CancellationToken cancellationToken = default);
}

public sealed class FederatedConsumerResolver(IConsumerRepository repository) : IFederatedConsumerResolver
{
    public Task<Consumer?> ResolveOidcAsync(
        OidcBearerIdentity identity,
        CancellationToken cancellationToken = default) =>
        repository.GetByOidcIdentityAsync(
            identity.Issuer.TrimEnd('/'),
            identity.Subject,
            cancellationToken);

    public Task<Consumer?> ResolveManagedIdentityAsync(
        ManagedIdentityBearerIdentity identity,
        CancellationToken cancellationToken = default) =>
        repository.GetByManagedIdentityAsync(identity.TenantId, identity.ObjectId, cancellationToken);
}