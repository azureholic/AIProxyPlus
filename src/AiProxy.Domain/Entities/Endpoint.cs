using AiProxy.Domain.Enums;
using AiProxy.Domain.Exceptions;

namespace AiProxy.Domain.Entities;

/// <summary>A reusable backend target and its outbound credential configuration.</summary>
public sealed class Endpoint
{
    public Guid Id { get; private set; }
    public string Name { get; private set; }
    public string BaseUrl { get; private set; }
    public AuthMode AuthMode { get; private set; }
    public string? SecretRef { get; private set; }
    public string? ManagedIdentityScope { get; private set; }
    public string? ManagedIdentityTenantId { get; private set; }
    public int TimeoutSeconds { get; private set; }
    public int MaxRetries { get; private set; }

    /// <summary>For EF Core materialization only — domain code must use <see cref="Create"/>.</summary>
    private Endpoint()
    {
        Name = null!;
        BaseUrl = null!;
    }

    private Endpoint(
        Guid id,
        string name,
        string baseUrl,
        AuthMode authMode,
        string? secretRef,
        string? managedIdentityScope,
        string? managedIdentityTenantId,
        int timeoutSeconds,
        int maxRetries)
    {
        Id = id;
        Name = name;
        BaseUrl = baseUrl;
        AuthMode = authMode;
        SecretRef = secretRef;
        ManagedIdentityScope = managedIdentityScope;
        ManagedIdentityTenantId = managedIdentityTenantId;
        TimeoutSeconds = timeoutSeconds;
        MaxRetries = maxRetries;
    }

    public static Endpoint Create(
        Guid id,
        string name,
        string baseUrl,
        AuthMode authMode,
        string? secretRef = null,
        string? managedIdentityScope = null,
        int timeoutSeconds = 100,
        int maxRetries = 2,
        string? managedIdentityTenantId = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("Endpoint.Name must not be empty.");
        }

        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new DomainException("Endpoint.BaseUrl must not be empty.");
        }

        if (timeoutSeconds < 1)
        {
            throw new DomainException("Endpoint.TimeoutSeconds must be at least 1.");
        }

        if (maxRetries < 0)
        {
            throw new DomainException("Endpoint.MaxRetries must not be negative.");
        }

        if (authMode == AuthMode.ApiKey && string.IsNullOrWhiteSpace(secretRef))
        {
            throw new DomainException("An ApiKey-mode endpoint must have a non-empty SecretRef.");
        }

        if (authMode == AuthMode.ManagedIdentity && string.IsNullOrWhiteSpace(managedIdentityScope))
        {
            throw new DomainException("A ManagedIdentity-mode endpoint must have a non-empty ManagedIdentityScope.");
        }

        if (authMode != AuthMode.ManagedIdentity && !string.IsNullOrWhiteSpace(managedIdentityTenantId))
        {
            throw new DomainException("Only ManagedIdentity-mode endpoints have a ManagedIdentityTenantId.");
        }

        if (!string.IsNullOrWhiteSpace(managedIdentityTenantId) && !Guid.TryParse(managedIdentityTenantId, out _))
        {
            throw new DomainException("Endpoint.ManagedIdentityTenantId must be a valid tenant GUID.");
        }

        return new Endpoint(
            id,
            name,
            baseUrl,
            authMode,
            secretRef,
            managedIdentityScope,
            string.IsNullOrWhiteSpace(managedIdentityTenantId) ? null : managedIdentityTenantId,
            timeoutSeconds,
            maxRetries);
    }

    public void UpdateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("Endpoint.Name must not be empty.");
        }

        Name = name;
    }

    public void UpdateTimeoutSeconds(int timeoutSeconds)
    {
        if (timeoutSeconds < 1)
        {
            throw new DomainException("Endpoint.TimeoutSeconds must be at least 1.");
        }

        TimeoutSeconds = timeoutSeconds;
    }

    public void UpdateMaxRetries(int maxRetries)
    {
        if (maxRetries < 0)
        {
            throw new DomainException("Endpoint.MaxRetries must not be negative.");
        }

        MaxRetries = maxRetries;
    }

    public void UpdateBaseUrl(string baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new DomainException("Endpoint.BaseUrl must not be empty.");
        }

        BaseUrl = baseUrl;
    }

    public void UpdateManagedIdentityScope(string managedIdentityScope)
    {
        if (AuthMode != AuthMode.ManagedIdentity)
        {
            throw new DomainException("Only ManagedIdentity-mode endpoints have a ManagedIdentityScope.");
        }

        if (string.IsNullOrWhiteSpace(managedIdentityScope))
        {
            throw new DomainException("A ManagedIdentity-mode endpoint must have a non-empty ManagedIdentityScope.");
        }

        ManagedIdentityScope = managedIdentityScope;
    }

    public void UpdateManagedIdentityTenantId(string? managedIdentityTenantId)
    {
        if (AuthMode != AuthMode.ManagedIdentity)
        {
            throw new DomainException("Only ManagedIdentity-mode endpoints have a ManagedIdentityTenantId.");
        }

        if (!string.IsNullOrWhiteSpace(managedIdentityTenantId) && !Guid.TryParse(managedIdentityTenantId, out _))
        {
            throw new DomainException("Endpoint.ManagedIdentityTenantId must be a valid tenant GUID.");
        }

        ManagedIdentityTenantId = string.IsNullOrWhiteSpace(managedIdentityTenantId) ? null : managedIdentityTenantId;
    }
}
