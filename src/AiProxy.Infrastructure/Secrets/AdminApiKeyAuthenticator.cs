using System.Security.Cryptography;
using System.Text;
using AiProxy.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace AiProxy.Infrastructure.Secrets;

/// <summary>
/// Validates the config API's admin key (architecture.md §7.2/§13). A single static secret (not a per-caller
/// credential like Consumer keys), so it's hashed with plain SHA-256 rather than the Consumer pepper scheme,
/// and compared in constant time. Auto-provisions a random key on first run — same pattern as the Consumer
/// key pepper (<see cref="ApiKeyHasher.CreateAsync"/>) — since there is no chicken-and-egg config API to
/// create it through.
/// </summary>
public sealed class AdminApiKeyAuthenticator(string adminApiKeyHash)
{
    private const string SecretRef = "admin-api-key-hash";

    public bool Validate(string? plaintextApiKey)
    {
        if (string.IsNullOrWhiteSpace(plaintextApiKey))
        {
            return false;
        }

        var computed = Encoding.UTF8.GetBytes(Hash(plaintextApiKey));
        var expected = Encoding.UTF8.GetBytes(adminApiKeyHash);
        return computed.Length == expected.Length && CryptographicOperations.FixedTimeEquals(computed, expected);
    }

    public static string Hash(string plaintextApiKey) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(plaintextApiKey)));

    public static async Task<AdminApiKeyAuthenticator> CreateAsync(
        ISecretResolver secretResolver, ILogger logger, CancellationToken cancellationToken = default)
    {
        var hash = await secretResolver.TryResolveAsync(SecretRef, cancellationToken);
        if (hash is null)
        {
            var plaintext = "aip_admin_" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
            hash = Hash(plaintext);
            await secretResolver.StoreAsync(SecretRef, hash, cancellationToken);
            logger.LogWarning(
                "No admin API key existed in the secret store; generated a new one. It will not be shown again: {AdminApiKey}",
                plaintext);
        }

        return new AdminApiKeyAuthenticator(hash);
    }
}
