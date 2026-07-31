namespace AiProxy.Infrastructure.Secrets;

/// <summary>Connection settings for the Vault KV v2 engine backing <see cref="VaultSecretResolver"/>.</summary>
public sealed class VaultOptions
{
    public required string Address { get; init; }

    public string MountPoint { get; init; } = "secret";

    public string PathPrefix { get; init; } = "ai-proxy";

    /// <summary>Static token auth (dev mode). Prefer <see cref="AppRoleId"/>/<see cref="AppRoleSecretId"/> in real deployments.</summary>
    public string? Token { get; init; }

    public string? AppRoleId { get; init; }

    public string? AppRoleSecretId { get; init; }
}
