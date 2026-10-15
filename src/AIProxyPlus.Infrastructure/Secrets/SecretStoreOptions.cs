namespace AIProxyPlus.Infrastructure.Secrets;

public enum SecretStoreProvider
{
    HashiCorpVault,
    AzureKeyVault,
}

public sealed class SecretStoreOptions
{
    public const string SectionName = "Secrets";

    public SecretStoreProvider Provider { get; init; } = SecretStoreProvider.HashiCorpVault;
}

public sealed class AzureKeyVaultOptions
{
    public const string SectionName = "AzureKeyVault";

    public Uri? VaultUri { get; init; }

    public bool UseManagedIdentity { get; init; } = true;

    public string? ManagedIdentityClientId { get; init; }
}
