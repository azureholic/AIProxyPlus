namespace AiProxy.Application.Consumers;

public sealed class WorkloadAuthenticationOptions
{
    public const string SectionName = "Authentication:Workload";
    public const string OidcScheme = "WorkloadOidc";
    public const string ManagedIdentityScheme = "ManagedIdentity";

    public WorkloadOidcOptions Oidc { get; init; } = new();
    public ManagedIdentityOptions ManagedIdentity { get; init; } = new();

    public void Validate()
    {
        Oidc.Validate("Authentication:Workload:Oidc");
        ManagedIdentity.Validate("Authentication:Workload:ManagedIdentity");
    }
}

public class WorkloadJwtProviderOptions
{
    public bool Enabled { get; init; }
    public string Authority { get; init; } = string.Empty;
    public string Audience { get; init; } = string.Empty;
    public string[] AllowedSigningAlgorithms { get; init; } = ["RS256", "PS256", "ES256"];

    internal void Validate(string sectionName)
    {
        if (!Enabled)
        {
            return;
        }

        if (!Uri.TryCreate(Authority, UriKind.Absolute, out var authority) ||
            (authority.Scheme != Uri.UriSchemeHttps && !authority.IsLoopback))
        {
            throw new InvalidOperationException($"{sectionName}:Authority must be an absolute HTTPS or loopback URI.");
        }

        if (string.IsNullOrWhiteSpace(Audience) || AllowedSigningAlgorithms.Length == 0)
        {
            throw new InvalidOperationException($"{sectionName}:Audience and AllowedSigningAlgorithms are required.");
        }
    }
}

public sealed class WorkloadOidcOptions : WorkloadJwtProviderOptions
{
    public string RequiredScope { get; init; } = "aiproxy.invoke";
}

public sealed class ManagedIdentityOptions : WorkloadJwtProviderOptions
{
    public string RequiredRole { get; init; } = "AiProxy.Invoke";
}