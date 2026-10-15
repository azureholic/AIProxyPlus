using AIProxyPlus.Api.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;

namespace AIProxyPlus.Api.Tests.Configuration;

public sealed class SecretStoreConfigurationTests
{
    [Fact]
    public void CreateClient_UnsupportedProvider_ThrowsConfigurationError()
    {
        using var factory = new ApiTestFactory().WithWebHostBuilder(builder =>
            builder.UseSetting("Secrets:Provider", "unsupported"));

        var act = () => factory.CreateClient();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Secrets:Provider*");
    }

    [Fact]
    public void CreateClient_AzureKeyVaultWithoutVaultUri_ThrowsConfigurationError()
    {
        using var factory = new ApiTestFactory().WithWebHostBuilder(builder =>
            builder.UseSetting("Secrets:Provider", "AzureKeyVault"));

        var act = () => factory.CreateClient();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*AzureKeyVault:VaultUri*");
    }

    [Fact]
    public void CreateClient_AzureKeyVaultWithoutManagedIdentityOutsideDevelopment_ThrowsConfigurationError()
    {
        using var factory = new ApiTestFactory().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("Secrets:Provider", "AzureKeyVault");
            builder.UseSetting("AzureKeyVault:VaultUri", "https://example.vault.azure.net/");
            builder.UseSetting("AzureKeyVault:UseManagedIdentity", "false");
        });

        var act = () => factory.CreateClient();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*must use managed identity*");
    }
}
