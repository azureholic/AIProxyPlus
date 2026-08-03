using AiProxy.Domain.Exceptions;

namespace AiProxy.Domain.Entities;

/// <summary>A caller allowed to invoke the proxy through an API key or OIDC identity.</summary>
public sealed class Consumer
{
    public Guid Id { get; private set; }
    public string Name { get; private set; }
    public string? ApiKeyHash { get; private set; }
    public string? OidcIssuer { get; private set; }
    public string? OidcSubject { get; private set; }
    public bool IsOidc => OidcIssuer is not null;
    public bool Enabled { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }

    /// <summary>For EF Core materialization only — domain code must use <see cref="Create"/>.</summary>
    private Consumer()
    {
        Name = null!;
    }

    private Consumer(
        Guid id,
        string name,
        string? apiKeyHash,
        string? oidcIssuer,
        string? oidcSubject,
        DateTimeOffset createdAt)
    {
        Id = id;
        Name = name;
        ApiKeyHash = apiKeyHash;
        OidcIssuer = oidcIssuer;
        OidcSubject = oidcSubject;
        Enabled = true;
        CreatedAt = createdAt;
    }

    public static Consumer Create(Guid id, string name, string apiKeyHash, DateTimeOffset? createdAt = null)
    {
        return CreateApiKey(id, name, apiKeyHash, createdAt);
    }

    public static Consumer CreateApiKey(
        Guid id,
        string name,
        string apiKeyHash,
        DateTimeOffset? createdAt = null)
    {
        ValidateName(name);

        if (string.IsNullOrWhiteSpace(apiKeyHash))
        {
            throw new DomainException("Consumer.ApiKeyHash must not be empty.");
        }

        return new Consumer(id, name, apiKeyHash, null, null, createdAt ?? DateTimeOffset.UtcNow);
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
            issuer.AbsoluteUri.TrimEnd('/'),
            oidcSubject,
            createdAt ?? DateTimeOffset.UtcNow);
    }

    public void RotateKey(string newApiKeyHash)
    {
        if (IsOidc)
        {
            throw new DomainException("OIDC consumers do not have API keys.");
        }

        if (string.IsNullOrWhiteSpace(newApiKeyHash))
        {
            throw new DomainException("Consumer.ApiKeyHash must not be empty.");
        }

        ApiKeyHash = newApiKeyHash;
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

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("Consumer.Name must not be empty.");
        }
    }
}
