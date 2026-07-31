namespace AiProxy.Application.Abstractions;

/// <summary>
/// One-way hashes a Consumer's plaintext API key for storage, and verifies a candidate key against a stored
/// hash. Never reversible — see architecture.md §8 (pepper lives in Vault; the hash lives in Postgres).
/// </summary>
public interface IApiKeyHasher
{
    string Hash(string plaintextApiKey);

    bool Verify(string plaintextApiKey, string hash);
}
