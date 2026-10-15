using AIProxyPlus.Api.Authentication;
using FluentAssertions;

namespace AIProxyPlus.Api.Tests.Authentication;

public sealed class OidcAuthenticationOptionsTests
{
    [Fact]
    public void Validate_EnabledWithEntraConfiguration_DoesNotThrow()
    {
        var options = CreateValidOptions();

        var act = options.Validate;

        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_EnabledWithInsecureRemoteAuthority_ThrowsConfigurationError()
    {
        var options = CreateValidOptions(authority: "http://identity.example.com");

        var act = options.Validate;

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*absolute HTTPS or loopback URI*");
    }

    [Fact]
    public void Validate_EnabledWithoutOpenIdScope_ThrowsConfigurationError()
    {
        var options = CreateValidOptions(scopes: ["profile"]);

        var act = options.Validate;

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*include 'openid'*");
    }

    [Fact]
    public void Validate_EnabledWithoutSigningAlgorithms_ThrowsConfigurationError()
    {
        var options = CreateValidOptions(allowedSigningAlgorithms: []);

        var act = options.Validate;

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*AllowedSigningAlgorithms*");
    }

    private static OidcAuthenticationOptions CreateValidOptions(
        string authority = "https://login.microsoftonline.com/tenant/v2.0",
        string[]? scopes = null,
        string[]? allowedSigningAlgorithms = null) =>
        new()
        {
            Enabled = true,
            Authority = authority,
            ClientId = "client-id",
            ClientSecret = "client-secret",
            Scopes = scopes ?? ["openid", "profile"],
            AllowedSigningAlgorithms = allowedSigningAlgorithms ?? ["RS256"],
        };
}
