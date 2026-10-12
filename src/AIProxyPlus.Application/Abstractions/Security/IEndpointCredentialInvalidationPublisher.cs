namespace AIProxyPlus.Application.Abstractions;

public sealed record EndpointCredentialInvalidation(Guid EndpointId, IReadOnlyCollection<string> SecretRefs);

public interface IEndpointCredentialInvalidationPublisher
{
    Task PublishAsync(EndpointCredentialInvalidation invalidation, CancellationToken cancellationToken = default);
}