using AiProxy.Domain.Exceptions;

namespace AiProxy.Domain.Entities;

/// <summary>
/// A sticky-session affinity mapping (architecture.md §9). <see cref="SessionId"/> is just an opaque key —
/// it may come from a client-supplied session identifier or, for the Responses API, an OpenAI response id
/// (§6.4) — the domain doesn't care which.
/// </summary>
public sealed class SessionAffinityEntry
{
    public string SessionId { get; private set; }
    public Guid ModelId { get; private set; }
    public Guid EndpointId { get; private set; }
    public DateTimeOffset LastUsedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }

    /// <summary>For EF Core materialization only — domain code must use <see cref="Create"/>.</summary>
    private SessionAffinityEntry()
    {
        SessionId = null!;
    }

    private SessionAffinityEntry(
        string sessionId,
        Guid modelId,
        Guid endpointId,
        DateTimeOffset lastUsedAt,
        DateTimeOffset expiresAt)
    {
        SessionId = sessionId;
        ModelId = modelId;
        EndpointId = endpointId;
        LastUsedAt = lastUsedAt;
        ExpiresAt = expiresAt;
    }

    public static SessionAffinityEntry Create(
        string sessionId,
        Guid modelId,
        Guid endpointId,
        TimeSpan ttl,
        DateTimeOffset? now = null)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new DomainException("SessionAffinityEntry.SessionId must not be empty.");
        }

        if (endpointId == Guid.Empty)
        {
            throw new DomainException("SessionAffinityEntry.EndpointId must be a valid Endpoint id.");
        }

        if (ttl <= TimeSpan.Zero)
        {
            throw new DomainException("SessionAffinityEntry TTL must be positive.");
        }

        var effectiveNow = now ?? DateTimeOffset.UtcNow;
        return new SessionAffinityEntry(sessionId, modelId, endpointId, effectiveNow, effectiveNow + ttl);
    }

    public void Touch(TimeSpan ttl, DateTimeOffset? now = null)
    {
        var effectiveNow = now ?? DateTimeOffset.UtcNow;
        LastUsedAt = effectiveNow;
        ExpiresAt = effectiveNow + ttl;
    }

    public bool IsExpired(DateTimeOffset? now = null) => (now ?? DateTimeOffset.UtcNow) >= ExpiresAt;
}
