using AiProxy.Domain.Exceptions;

namespace AiProxy.Domain.Entities;

/// <summary>A caller allowed to invoke the proxy, authenticated via a hashed API key (architecture.md §8).</summary>
public sealed class Consumer
{
    public Guid Id { get; private set; }
    public string Name { get; private set; }
    public string ApiKeyHash { get; private set; }
    public bool Enabled { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }

    /// <summary>For EF Core materialization only — domain code must use <see cref="Create"/>.</summary>
    private Consumer()
    {
        Name = null!;
        ApiKeyHash = null!;
    }

    private Consumer(Guid id, string name, string apiKeyHash, DateTimeOffset createdAt)
    {
        Id = id;
        Name = name;
        ApiKeyHash = apiKeyHash;
        Enabled = true;
        CreatedAt = createdAt;
    }

    public static Consumer Create(Guid id, string name, string apiKeyHash, DateTimeOffset? createdAt = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("Consumer.Name must not be empty.");
        }

        if (string.IsNullOrWhiteSpace(apiKeyHash))
        {
            throw new DomainException("Consumer.ApiKeyHash must not be empty.");
        }

        return new Consumer(id, name, apiKeyHash, createdAt ?? DateTimeOffset.UtcNow);
    }

    public void RotateKey(string newApiKeyHash)
    {
        if (string.IsNullOrWhiteSpace(newApiKeyHash))
        {
            throw new DomainException("Consumer.ApiKeyHash must not be empty.");
        }

        ApiKeyHash = newApiKeyHash;
    }

    public void Revoke(DateTimeOffset? revokedAt = null)
    {
        Enabled = false;
        RevokedAt = revokedAt ?? DateTimeOffset.UtcNow;
    }
}
