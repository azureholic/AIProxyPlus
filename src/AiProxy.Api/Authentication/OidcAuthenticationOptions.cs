namespace AiProxy.Api.Authentication;

public sealed class OidcAuthenticationOptions
{
    public const string SectionName = "Authentication:Oidc";

    public bool Enabled { get; init; }
    public string Authority { get; init; } = string.Empty;
    public string ClientId { get; init; } = string.Empty;
    public string ClientSecret { get; init; } = string.Empty;
    public string[] Scopes { get; init; } = ["openid", "profile"];
    public string UsernameClaim { get; init; } = "preferred_username";
    public bool GetClaimsFromUserInfo { get; init; }
    public string CallbackPath { get; init; } = "/signin-oidc";
    public string SignedOutCallbackPath { get; init; } = "/signout-callback-oidc";
    public int SessionLifetimeMinutes { get; init; } = 480;
    public string[] AllowedSigningAlgorithms { get; init; } = ["RS256", "PS256", "ES256"];

    public void Validate()
    {
        if (!Enabled)
        {
            return;
        }

        if (!Uri.TryCreate(Authority, UriKind.Absolute, out var authority) ||
            (authority.Scheme != Uri.UriSchemeHttps && !authority.IsLoopback))
        {
            throw new InvalidOperationException("Authentication:Oidc:Authority must be an absolute HTTPS or loopback URI.");
        }

        if (string.IsNullOrWhiteSpace(ClientId) || string.IsNullOrWhiteSpace(ClientSecret))
        {
            throw new InvalidOperationException("OIDC ClientId and ClientSecret are required when authentication is enabled.");
        }

        if (!Scopes.Contains("openid", StringComparer.Ordinal))
        {
            throw new InvalidOperationException("OIDC scopes must include 'openid'.");
        }

        if (string.IsNullOrWhiteSpace(UsernameClaim) || AllowedSigningAlgorithms.Length == 0)
        {
            throw new InvalidOperationException("OIDC UsernameClaim and AllowedSigningAlgorithms are required.");
        }
    }
}