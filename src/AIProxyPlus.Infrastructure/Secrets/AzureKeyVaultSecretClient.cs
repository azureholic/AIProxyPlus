using Azure.Security.KeyVault.Secrets;

namespace AIProxyPlus.Infrastructure.Secrets;

public interface IAzureKeyVaultSecretClient
{
    Task<string> GetAsync(string name, CancellationToken cancellationToken);

    Task SetAsync(string name, string value, CancellationToken cancellationToken);

    Task DeleteAsync(string name, CancellationToken cancellationToken);
}

public sealed class AzureKeyVaultSecretClient(SecretClient client) : IAzureKeyVaultSecretClient
{
    public async Task<string> GetAsync(string name, CancellationToken cancellationToken)
    {
        var response = await client.GetSecretAsync(name, cancellationToken: cancellationToken);
        return response.Value.Value;
    }

    public async Task SetAsync(string name, string value, CancellationToken cancellationToken)
    {
        await client.SetSecretAsync(name, value, cancellationToken);
    }

    public async Task DeleteAsync(string name, CancellationToken cancellationToken)
    {
        await client.StartDeleteSecretAsync(name, cancellationToken);
    }
}
