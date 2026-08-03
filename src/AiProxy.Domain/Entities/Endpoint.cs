using AiProxy.Domain.Enums;
using AiProxy.Domain.Exceptions;

namespace AiProxy.Domain.Entities;

/// <summary>A single backend target that can serve a <see cref="ModelDeployment"/>.</summary>
public sealed class Endpoint
{
    public Guid Id { get; private set; }
    public Guid ModelDeploymentId { get; private set; }
    public string BaseUrl { get; private set; }
    public ApiFamily ApiFamily { get; private set; }
    public int Priority { get; private set; }
    public AuthMode AuthMode { get; private set; }
    public string? SecretRef { get; private set; }
    public string? ManagedIdentityScope { get; private set; }
    public string? ManagedIdentityTenantId { get; private set; }
    public bool Enabled { get; private set; }
    public int TimeoutSeconds { get; private set; }
    public int MaxRetries { get; private set; }
    public EndpointHealthState Health { get; private set; }

    /// <summary>For EF Core materialization only — domain code must use <see cref="Create"/>.</summary>
    private Endpoint()
    {
        BaseUrl = null!;
    }

    private Endpoint(
        Guid id,
        Guid modelDeploymentId,
        string baseUrl,
        ApiFamily apiFamily,
        int priority,
        AuthMode authMode,
        string? secretRef,
        string? managedIdentityScope,
        string? managedIdentityTenantId,
        bool enabled,
        int timeoutSeconds,
        int maxRetries)
    {
        Id = id;
        ModelDeploymentId = modelDeploymentId;
        BaseUrl = baseUrl;
        ApiFamily = apiFamily;
        Priority = priority;
        AuthMode = authMode;
        SecretRef = secretRef;
        ManagedIdentityScope = managedIdentityScope;
        ManagedIdentityTenantId = managedIdentityTenantId;
        Enabled = enabled;
        TimeoutSeconds = timeoutSeconds;
        MaxRetries = maxRetries;
        Health = EndpointHealthState.Unknown;
    }

    public static Endpoint Create(
        Guid id,
        Guid modelDeploymentId,
        string baseUrl,
        ApiFamily apiFamily,
        int priority,
        AuthMode authMode,
        string? secretRef = null,
        string? managedIdentityScope = null,
        bool enabled = true,
        int timeoutSeconds = 100,
        int maxRetries = 2,
        string? managedIdentityTenantId = null)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new DomainException("Endpoint.BaseUrl must not be empty.");
        }

        if (priority < 1)
        {
            throw new DomainException("Endpoint.Priority must be 1 or greater (lower is tried first).");
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
            modelDeploymentId,
            baseUrl,
            apiFamily,
            priority,
            authMode,
            secretRef,
            managedIdentityScope,
            string.IsNullOrWhiteSpace(managedIdentityTenantId) ? null : managedIdentityTenantId,
            enabled,
            timeoutSeconds,
            maxRetries);
    }

    public void MarkHealthy() => Health = EndpointHealthState.Healthy;

    public void MarkUnhealthy() => Health = EndpointHealthState.Unhealthy;

    public void Disable() => Enabled = false;

    public void Enable() => Enabled = true;

    /// <summary>Changes this endpoint's priority. Cross-endpoint uniqueness is enforced by <see cref="ModelDeployment.ChangeEndpointPriority"/>.</summary>
    internal void UpdatePriority(int priority)
    {
        if (priority < 1)
        {
            throw new DomainException("Endpoint.Priority must be 1 or greater (lower is tried first).");
        }

        Priority = priority;
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
