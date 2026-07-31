using System.Security.Cryptography;
using AiProxy.Application.Abstractions;

namespace AiProxy.Infrastructure.Secrets;

/// <summary>Generates a cryptographically random, URL-safe plaintext API key for a new/rotated Consumer.</summary>
public sealed class ApiKeyGenerator : IApiKeyGenerator
{
    private const string Prefix = "aip_";

    public string Generate()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        var base64Url = Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
        return Prefix + base64Url;
    }
}
