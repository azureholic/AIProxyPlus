namespace AiProxy.Domain.Enums;

/// <summary>How the proxy authenticates itself to an <see cref="Entities.Endpoint"/>.</summary>
public enum AuthMode
{
    ApiKey,
    ManagedIdentity
}
