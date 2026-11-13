using AIProxyPlus.Api.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using AIProxyPlus.Infrastructure.Secrets;
using Microsoft.Extensions.DependencyInjection;

namespace AIProxyPlus.Api.Tests.Configuration;

public sealed class SecretStoreConfigurationTests
{
    [Theory]
    [InlineData(false, 37, 7)]
    [InlineData(true, 12, 3)]
    public void CreateClient_RetirementConfiguration_PreservesLegacyFallbackAndNewPrecedence(
        bool configureNewSection, int graceMinutes, int retrySeconds)
    {
        using var factory = new ApiTestFactory().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Vault:Retirement:GracePeriod", "00:37:00");
            builder.UseSetting("Vault:Retirement:RetryDelay", "00:00:07");
            if (configureNewSection)
            {
                builder.UseSetting("Secrets:Retirement:GracePeriod", "00:12:00");
                builder.UseSetting("Secrets:Retirement:RetryDelay", "00:00:03");
            }
        });
        using var client = factory.CreateClient();

        var options = factory.Services.GetRequiredService<SecretRetirementOptions>();

        options.GracePeriod.Should().Be(TimeSpan.FromMinutes(graceMinutes));
        options.RetryDelay.Should().Be(TimeSpan.FromSeconds(retrySeconds));
    }

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
