using System.Security.Cryptography;
using System.Xml.Linq;
using AIProxyPlus.Application.Abstractions;
using Microsoft.AspNetCore.DataProtection.XmlEncryption;
using Microsoft.Extensions.DependencyInjection;

namespace AIProxyPlus.Infrastructure.Security;

/// <summary>Protects Data Protection key material at rest in the configured secret store, not in PostgreSQL.</summary>
/// <param name="secrets">The shared secret store used by every replica.</param>
public sealed class SecretStoreXmlEncryptor(ISecretResolver secrets) : IXmlEncryptor
{
    /// <summary>Replaces secret XML with an immutable reference to secret-store-protected material.</summary>
    /// <param name="plaintextElement">The secret key material supplied by Data Protection.</param>
    /// <returns>A reference which Data Protection can resolve on any replica.</returns>
    public EncryptedXmlInfo Encrypt(XElement plaintextElement)
    {
        var reference = $"data-protection/{Guid.NewGuid():N}";
        // Data Protection's XML encryption contract is synchronous.
        secrets.StoreAsync(reference, plaintextElement.ToString(SaveOptions.DisableFormatting)).GetAwaiter().GetResult();
        return new EncryptedXmlInfo(
            new XElement("secretStore", new XAttribute("reference", reference)),
            typeof(SecretStoreXmlDecryptor));
    }
}

/// <summary>Resolves Data Protection key material using the replica's configured secret store.</summary>
public sealed class SecretStoreXmlDecryptor : IXmlDecryptor
{
    private readonly ISecretResolver _secrets;

    /// <summary>Creates a decryptor through Data Protection's service-provider activation contract.</summary>
    /// <param name="services">The current replica's service provider.</param>
    public SecretStoreXmlDecryptor(IServiceProvider services) =>
        _secrets = services.GetRequiredService<ISecretResolver>();

    /// <summary>Loads immutable key material; missing or inaccessible secrets fail explicitly.</summary>
    /// <param name="encryptedElement">The persisted secret-store reference.</param>
    /// <returns>The key material originally protected by the encryptor.</returns>
    public XElement Decrypt(XElement encryptedElement)
    {
        var reference = (string?)encryptedElement.Attribute("reference");
        const string prefix = "data-protection/";
        if (encryptedElement.Name != "secretStore" || reference is null ||
            !reference.StartsWith(prefix, StringComparison.Ordinal) ||
            !Guid.TryParseExact(reference[prefix.Length..], "N", out _))
        {
            throw new CryptographicException("Invalid Data Protection secret-store reference.");
        }

        var material = _secrets.TryResolveAsync(reference).GetAwaiter().GetResult()
            ?? throw new CryptographicException("Data Protection key material is missing from the secret store.");
        return XElement.Parse(material);
    }
}
