using System.Security.Cryptography;
using System.Text;
using AIProxyPlus.Application.Abstractions;

namespace AIProxyPlus.Infrastructure.Secrets;

/// <summary>
/// HMAC-SHA256 API key hashing with a shared server-side pepper (architecture.md §8). Deterministic by
/// design — the same key always hashes to the same value — so <see cref="Application.Consumers.ConsumerAuthenticator"/>
/// can look up a consumer by hash directly instead of iterating and verifying every row.
/// </summary>
public sealed class ApiKeyHasher : IApiKeyHasher
{
    private const string PepperSecretRef = "consumer-key-pepper";

    private readonly byte[] _pepperBytes;

    public ApiKeyHasher(string pepper)
    {
        if (string.IsNullOrWhiteSpace(pepper))
        {
            throw new ArgumentException("Pepper must not be empty.", nameof(pepper));
        }

        _pepperBytes = Encoding.UTF8.GetBytes(pepper);
    }

    public string Hash(string plaintextApiKey)
    {
        using var hmac = new HMACSHA256(_pepperBytes);
        var hashBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(plaintextApiKey));
        return Convert.ToHexStringLower(hashBytes);
    }

    public bool Verify(string plaintextApiKey, string hash)
    {
        var computed = Encoding.UTF8.GetBytes(Hash(plaintextApiKey));
        var expected = Encoding.UTF8.GetBytes(hash);
        return computed.Length == expected.Length && CryptographicOperations.FixedTimeEquals(computed, expected);
    }

    /// <summary>Resolves the shared pepper, optionally auto-provisioning it for single-instance local development.</summary>
    public static async Task<ApiKeyHasher> CreateAsync(
        ISecretResolver secretResolver,
        CancellationToken cancellationToken = default,
        bool autoProvision = true)
    {
        var pepper = await secretResolver.TryResolveAsync(PepperSecretRef, cancellationToken);
        if (pepper is null)
        {
            if (!autoProvision)
            {
                throw new InvalidOperationException(
                    "The consumer-key pepper must be provisioned in Azure Key Vault before starting the API. Run the AZD pepper bootstrap hook.");
            }

            pepper = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
            await secretResolver.StoreAsync(PepperSecretRef, pepper, cancellationToken);
        }

        return new ApiKeyHasher(pepper);
    }
}
