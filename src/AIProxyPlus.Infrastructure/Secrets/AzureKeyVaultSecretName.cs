using System.Security.Cryptography;
using System.Text;

namespace AIProxyPlus.Infrastructure.Secrets;

public static class AzureKeyVaultSecretName
{
    private const int MaxReferenceLength = 512;

    public static string FromReference(string secretRef)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secretRef);
        if (secretRef.Length > MaxReferenceLength)
        {
            throw new ArgumentException(
                $"Secret references must not exceed {MaxReferenceLength} characters.",
                nameof(secretRef));
        }

        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(secretRef));
        return $"aiproxyplus-{Convert.ToHexStringLower(digest)}";
    }
}
