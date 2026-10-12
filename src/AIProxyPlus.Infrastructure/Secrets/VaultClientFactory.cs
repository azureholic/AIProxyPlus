using VaultSharp;
using VaultSharp.V1.AuthMethods;
using VaultSharp.V1.AuthMethods.AppRole;
using VaultSharp.V1.AuthMethods.Token;

namespace AIProxyPlus.Infrastructure.Secrets;

public static class VaultClientFactory
{
    public static IVaultClient Create(VaultOptions options)
    {
        IAuthMethodInfo authMethod = options.Token is { Length: > 0 }
            ? new TokenAuthMethodInfo(options.Token)
            : new AppRoleAuthMethodInfo(
                options.AppRoleId ?? throw new InvalidOperationException("VaultOptions.AppRoleId is required when Token is not set."),
                options.AppRoleSecretId ?? throw new InvalidOperationException("VaultOptions.AppRoleSecretId is required when Token is not set."));

        var settings = new VaultClientSettings(options.Address, authMethod);
        return new VaultClient(settings);
    }
}
