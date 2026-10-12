using System.Net;
using AIProxyPlus.Application.Abstractions;
using Microsoft.Extensions.Logging;
using VaultSharp;
using VaultSharp.Core;

namespace AIProxyPlus.Infrastructure.Secrets;

/// <summary>
/// Resolves/stores secret material in Vault's KV v2 engine (architecture.md §8). Only ever logs the secret
/// <em>path</em>, never its value.
/// </summary>
public sealed class VaultSecretResolver(IVaultClient vaultClient, VaultOptions options, ILogger<VaultSecretResolver> logger) : ISecretResolver
{
    private const string ValueKey = "value";

    public async Task<string?> TryResolveAsync(string secretRef, CancellationToken cancellationToken = default)
    {
        try
        {
            var secret = await vaultClient.V1.Secrets.KeyValue.V2.ReadSecretAsync(
                path: FullPath(secretRef), mountPoint: options.MountPoint);

            logger.LogDebug("Resolved secret at path '{SecretRef}'.", secretRef);
            return secret.Data.Data.TryGetValue(ValueKey, out var value) ? value?.ToString() : null;
        }
        catch (VaultApiException ex) when (ex.HttpStatusCode == HttpStatusCode.NotFound)
        {
            logger.LogDebug("No secret found at path '{SecretRef}'.", secretRef);
            return null;
        }
    }

    public async Task StoreAsync(string secretRef, string plaintextValue, CancellationToken cancellationToken = default)
    {
        await vaultClient.V1.Secrets.KeyValue.V2.WriteSecretAsync(
            path: FullPath(secretRef),
            data: new Dictionary<string, object> { [ValueKey] = plaintextValue },
            mountPoint: options.MountPoint);

        logger.LogInformation("Stored secret at path '{SecretRef}'.", secretRef);
    }

    public async Task DeleteAsync(string secretRef, CancellationToken cancellationToken = default)
    {
        await vaultClient.V1.Secrets.KeyValue.V2.DeleteMetadataAsync(FullPath(secretRef), options.MountPoint);
        logger.LogInformation("Deleted secret at path '{SecretRef}'.", secretRef);
    }

    private string FullPath(string secretRef) => $"{options.PathPrefix}/{secretRef}";
}
