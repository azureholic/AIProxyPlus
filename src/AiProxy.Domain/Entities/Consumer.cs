using AiProxy.Domain.Enums;
using AiProxy.Domain.Exceptions;

namespace AiProxy.Domain.Entities;

/// <summary>A caller allowed to invoke the proxy through an API key or OIDC identity.</summary>
public sealed class Consumer
{
    private readonly List<ConsumerAllowedModel> _allowedModels = [];

    public Guid Id { get; private set; }
    public string Name { get; private set; }
    public string? ApiKeyHash { get; private set; }

    /// <summary>The second, independently-rotatable key slot ("Key2", Azure storage-account-key style) —
    /// null until an admin first rotates it (architecture.md §8). Either slot authenticates the caller.</summary>
    public string? ApiKeyHash2 { get; private set; }
    public string? OidcIssuer { get; private set; }
    public string? OidcSubject { get; private set; }
    public bool IsOidc => OidcIssuer is not null;
    public bool Enabled { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public IReadOnlyList<Guid> AllowedModelIds => _allowedModels.Select(a => a.ModelId).ToArray();

    /// <summary>True when no allow-list rows exist — the default, backward-compatible state where this
    /// consumer may invoke any model (architecture.md §13).</summary>
    public bool HasUnrestrictedModelAccess => _allowedModels.Count == 0;

    /// <summary>For EF Core materialization only — domain code must use <see cref="Create"/>.</summary>
    private Consumer()
    {
        Name = null!;
    }

    private Consumer(
        Guid id,
        string name,
        string? apiKeyHash,
        string? apiKeyHash2,
        string? oidcIssuer,
        string? oidcSubject,
        DateTimeOffset createdAt)
    {
        Id = id;
        Name = name;
        ApiKeyHash = apiKeyHash;
        ApiKeyHash2 = apiKeyHash2;
        OidcIssuer = oidcIssuer;
        OidcSubject = oidcSubject;
        Enabled = true;
        CreatedAt = createdAt;
    }

    public static Consumer Create(Guid id, string name, string apiKeyHash, DateTimeOffset? createdAt = null)
    {
        return CreateApiKey(id, name, apiKeyHash, null, createdAt);
    }

    public static Consumer CreateApiKey(
        Guid id,
        string name,
        string apiKeyHash,
        string? apiKeyHash2 = null,
        DateTimeOffset? createdAt = null)
    {
        ValidateName(name);
        ValidateApiKeyHash(apiKeyHash);

        if (apiKeyHash2 is not null)
        {
            ValidateApiKeyHash(apiKeyHash2);
        }

        return new Consumer(id, name, apiKeyHash, apiKeyHash2, null, null, createdAt ?? DateTimeOffset.UtcNow);
    }

    public static Consumer CreateOidc(
        Guid id,
        string name,
        string oidcIssuer,
        string oidcSubject,
        DateTimeOffset? createdAt = null)
    {
        ValidateName(name);

        if (!Uri.TryCreate(oidcIssuer, UriKind.Absolute, out var issuer) ||
            (issuer.Scheme != Uri.UriSchemeHttps && !issuer.IsLoopback))
        {
            throw new DomainException("Consumer.OidcIssuer must be an absolute HTTPS URI or a loopback URI.");
        }

        if (string.IsNullOrWhiteSpace(oidcSubject))
        {
            throw new DomainException("Consumer.OidcSubject must not be empty.");
        }

        return new Consumer(
            id,
            name,
            null,
            null,
            issuer.AbsoluteUri.TrimEnd('/'),
            oidcSubject,
            createdAt ?? DateTimeOffset.UtcNow);
    }

    /// <summary>Rotates one key slot independently — the other slot's hash is untouched and remains valid,
    /// giving zero-downtime rotation (architecture.md §8).</summary>
    public void RotateKey(ApiKeySlot slot, string newApiKeyHash)
    {
        if (IsOidc)
        {
            throw new DomainException("OIDC consumers do not have API keys.");
        }

        ValidateApiKeyHash(newApiKeyHash);

        switch (slot)
        {
            case ApiKeySlot.Primary:
                ApiKeyHash = newApiKeyHash;
                break;
            case ApiKeySlot.Secondary:
                ApiKeyHash2 = newApiKeyHash;
                break;
            default:
                throw new DomainException($"Unknown API key slot '{slot}'.");
        }
    }

    public void RefreshName(string name)
    {
        ValidateName(name);
        Name = name;
    }

    public void Revoke(DateTimeOffset? revokedAt = null)
    {
        Enabled = false;
        RevokedAt = revokedAt ?? DateTimeOffset.UtcNow;
    }

    /// <summary>Idempotent — allowing an already-allowed model is a no-op.</summary>
    public void AllowModel(Guid modelId)
    {
        if (_allowedModels.All(a => a.ModelId != modelId))
        {
            _allowedModels.Add(new ConsumerAllowedModel(Id, modelId));
        }
    }

    /// <summary>Idempotent — revoking access to a model that wasn't allowed is a no-op.</summary>
    public void RevokeModelAccess(Guid modelId) => _allowedModels.RemoveAll(a => a.ModelId == modelId);

    public bool CanUseModel(Guid modelId) => HasUnrestrictedModelAccess || _allowedModels.Any(a => a.ModelId == modelId);

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("Consumer.Name must not be empty.");
        }
    }

    private static void ValidateApiKeyHash(string apiKeyHash)
    {
        if (string.IsNullOrWhiteSpace(apiKeyHash))
        {
            throw new DomainException("Consumer.ApiKeyHash must not be empty.");
        }
    }
}
