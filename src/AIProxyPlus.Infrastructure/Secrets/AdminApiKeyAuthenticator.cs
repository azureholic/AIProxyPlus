using System.Security.Cryptography;
using System.Text;
using AIProxyPlus.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace AIProxyPlus.Infrastructure.Secrets;

/// <summary>
/// Validates the config API's admin key (architecture.md §7.2/§13). A single static secret (not a per-caller
/// credential like Consumer keys), so it's hashed with plain SHA-256 rather than the Consumer pepper scheme,
/// and compared in constant time. The plaintext key is never generated, returned, or logged by this process: an
/// operator supplies it through <c>Authentication:AdminApiKey</c> and only its hash is stored in the secret store.
/// </summary>
public sealed class AdminApiKeyAuthenticator(string adminApiKeyHash)
{
    private const string SecretRef = "admin-api-key-hash";

    public bool Validate(string? plaintextApiKey)
    {
        if (string.IsNullOrWhiteSpace(plaintextApiKey) || string.IsNullOrEmpty(adminApiKeyHash))
        {
            return false;
        }

        var computed = Encoding.UTF8.GetBytes(Hash(plaintextApiKey));
        var expected = Encoding.UTF8.GetBytes(adminApiKeyHash);
        return computed.Length == expected.Length && CryptographicOperations.FixedTimeEquals(computed, expected);
    }

    public static string Hash(string plaintextApiKey) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(plaintextApiKey)));

    /// <param name="bootstrapApiKey">
    /// Operator-supplied key. When present it is the source of truth: its hash replaces any stored hash, which is
    /// how the key is rotated.
    /// </param>
    public static async Task<AdminApiKeyAuthenticator> CreateAsync(
        ISecretResolver secretResolver,
        ILogger logger,
        string? bootstrapApiKey = null,
        CancellationToken cancellationToken = default)
    {
        var storedHash = await secretResolver.TryResolveAsync(SecretRef, cancellationToken);

        if (!string.IsNullOrWhiteSpace(bootstrapApiKey))
        {
            var configuredHash = Hash(bootstrapApiKey);
            if (!string.Equals(storedHash, configuredHash, StringComparison.Ordinal))
            {
                await secretResolver.StoreAsync(SecretRef, configuredHash, cancellationToken);
            }

            return new AdminApiKeyAuthenticator(configuredHash);
        }

        if (storedHash is null)
        {
            logger.LogWarning(
                "No admin API key is configured, so the break-glass admin key is unavailable. " +
                "Set Authentication:AdminApiKey to enable it.");
        }

        return new AdminApiKeyAuthenticator(storedHash ?? string.Empty);
    }
}
