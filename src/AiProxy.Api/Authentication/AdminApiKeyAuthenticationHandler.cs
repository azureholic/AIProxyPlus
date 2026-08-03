using System.Security.Claims;
using System.Text.Encodings.Web;
using AiProxy.Infrastructure.Secrets;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace AiProxy.Api.Authentication;

public sealed class AdminApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    AdminApiKeyAuthenticator authenticator,
    IConfiguration configuration)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "AdminApiKey";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!configuration.GetValue("Authentication:AdminApiKeyFallbackEnabled", false))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        Request.Headers.TryGetValue("X-Admin-Api-Key", out var header);
        if (header.Count == 0)
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        if (!authenticator.Validate(header[0]))
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid admin API key."));
        }

        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "admin-api-key")],
            SchemeName));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
    }
}