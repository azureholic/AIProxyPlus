using AIProxyPlus.Domain.Enums;
using AIProxyPlus.Domain.Exceptions;

namespace AIProxyPlus.Domain.Entities;

/// <summary>A reusable backend target and its outbound credential configuration.</summary>
public sealed class Endpoint
{
    public Guid Id { get; private set; }
    public string Name { get; private set; }
    public string BaseUrl { get; private set; }
    public AuthMode AuthMode { get; private set; }
    public string? PrimarySecretRef { get; private set; }
    public string? SecondarySecretRef { get; private set; }
    public EndpointCredentialSlot? ActiveCredentialSlot { get; private set; }
    public EndpointCredentialSlot? FallbackCredentialSlot { get; private set; }
    public DateTimeOffset? FallbackExpiresAt { get; private set; }
    public string? SecretRef => PrimarySecretRef;
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
        PrimarySecretRef = secretRef;
        ActiveCredentialSlot = authMode == AuthMode.ApiKey ? EndpointCredentialSlot.Primary : null;
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

    public void SetSecretReference(EndpointCredentialSlot slot, string secretRef)
    {
        EnsureApiKeyMode();
        if (string.IsNullOrWhiteSpace(secretRef))
        {
            throw new DomainException("Endpoint secret reference must not be empty.");
        }

        if (slot == EndpointCredentialSlot.Primary)
        {
            PrimarySecretRef = secretRef;
        }
        else
        {
            SecondarySecretRef = secretRef;
        }
    }

    public void ActivateCredential(
        EndpointCredentialSlot slot,
        DateTimeOffset? fallbackExpiresAt,
        DateTimeOffset now)
    {
        EnsureApiKeyMode();
        if (GetSecretReference(slot) is null)
        {
            throw new DomainException($"Credential slot '{slot}' is not configured.");
        }

        var previousSlot = ActiveCredentialSlot
            ?? throw new DomainException("An ApiKey-mode endpoint must have an active credential slot.");
        if (slot == previousSlot && fallbackExpiresAt is not null)
        {
            throw new DomainException("Credential fallback requires activating a different slot.");
        }
        if (fallbackExpiresAt is not null && fallbackExpiresAt <= now)
        {
            throw new DomainException("Credential fallback expiry must be in the future.");
        }

        ActiveCredentialSlot = slot;
        FallbackCredentialSlot = fallbackExpiresAt is null || slot == previousSlot ? null : previousSlot;
        FallbackExpiresAt = FallbackCredentialSlot is null ? null : fallbackExpiresAt;
    }

    public void DisableCredentialFallback()
    {
        EnsureApiKeyMode();
        FallbackCredentialSlot = null;
        FallbackExpiresAt = null;
    }

    public void RemoveSecondarySecret(DateTimeOffset now)
    {
        EnsureApiKeyMode();
        if (ActiveCredentialSlot == EndpointCredentialSlot.Secondary)
        {
            throw new DomainException("The active credential slot cannot be removed.");
        }
        if (FallbackCredentialSlot == EndpointCredentialSlot.Secondary && FallbackExpiresAt > now)
        {
            throw new DomainException("A credential slot with an active fallback window cannot be removed.");
        }

        SecondarySecretRef = null;
        if (FallbackCredentialSlot == EndpointCredentialSlot.Secondary)
        {
            DisableCredentialFallback();
        }
    }

    public string GetActiveSecretReference() =>
        GetSecretReference(ActiveCredentialSlot
            ?? throw new DomainException("Endpoint has no active credential slot."))
        ?? throw new DomainException("The active credential slot is not configured.");

    public string? GetEligibleFallbackSecretReference(DateTimeOffset now) =>
        FallbackCredentialSlot is { } slot && FallbackExpiresAt > now
            ? GetSecretReference(slot)
            : null;

    private string? GetSecretReference(EndpointCredentialSlot slot) =>
        slot == EndpointCredentialSlot.Primary ? PrimarySecretRef : SecondarySecretRef;

    private void EnsureApiKeyMode()
    {
        if (AuthMode != AuthMode.ApiKey)
        {
            throw new DomainException("Only ApiKey-mode endpoints have credential slots.");
        }
    }
}
