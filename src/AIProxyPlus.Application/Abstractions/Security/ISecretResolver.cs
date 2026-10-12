namespace AIProxyPlus.Application.Abstractions;

/// <summary>
/// Resolves/stores secret material in the configured secret store (Vault in Infrastructure — architecture.md §8).
/// <paramref name="secretRef"/> is a relative path (e.g. "endpoints/{endpointId}"); the implementation owns the
/// mount/prefix convention.
/// </summary>
public interface ISecretResolver
{
    Task<string?> TryResolveAsync(string secretRef, CancellationToken cancellationToken = default);

    Task StoreAsync(string secretRef, string plaintextValue, CancellationToken cancellationToken = default);

    Task DeleteAsync(string secretRef, CancellationToken cancellationToken = default);
}
