using AIProxyPlus.Application.Abstractions;
using Azure;
using Microsoft.Extensions.Logging;

namespace AIProxyPlus.Infrastructure.Secrets;

public sealed class AzureKeyVaultSecretResolver(
    IAzureKeyVaultSecretClient client,
    ILogger<AzureKeyVaultSecretResolver> logger) : ISecretResolver
{
    public async Task<string?> TryResolveAsync(string secretRef, CancellationToken cancellationToken = default)
    {
        var name = AzureKeyVaultSecretName.FromReference(secretRef);
        try
        {
            var value = await client.GetAsync(name, cancellationToken);
            logger.LogDebug("Resolved secret reference '{SecretRef}' from Azure Key Vault.", secretRef);
            return value;
        }
        catch (RequestFailedException exception) when (exception.Status == 404)
        {
            logger.LogDebug("No secret found for reference '{SecretRef}' in Azure Key Vault.", secretRef);
            return null;
        }
    }

    public async Task StoreAsync(
        string secretRef,
        string plaintextValue,
        CancellationToken cancellationToken = default)
    {
        var name = AzureKeyVaultSecretName.FromReference(secretRef);
        await client.SetAsync(name, plaintextValue, cancellationToken);
        logger.LogInformation("Stored secret reference '{SecretRef}' in Azure Key Vault.", secretRef);
    }

    public async Task DeleteAsync(string secretRef, CancellationToken cancellationToken = default)
    {
        var name = AzureKeyVaultSecretName.FromReference(secretRef);
        try
        {
            await client.DeleteAsync(name, cancellationToken);
            logger.LogInformation("Deleted secret reference '{SecretRef}' from Azure Key Vault.", secretRef);
        }
        catch (RequestFailedException exception) when (exception.Status == 404)
        {
            logger.LogDebug("Secret reference '{SecretRef}' was already absent from Azure Key Vault.", secretRef);
        }
    }
}
