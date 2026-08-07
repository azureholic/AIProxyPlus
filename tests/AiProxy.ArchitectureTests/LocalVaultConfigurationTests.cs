using FluentAssertions;

namespace AiProxy.ArchitectureTests;

public sealed class LocalVaultConfigurationTests
{
    [Fact]
    public void LocalVault_WhenConfigured_UsesDurableFileBackend()
    {
        var appHost = ReadFixture("src", "AiProxy.AppHost", "AppHost.cs");
        var compose = ReadFixture("docker-compose.yml");
        var vaultConfig = ReadFixture("src", "AiProxy.AppHost", "vault", "local.hcl");

        appHost.Should().Contain("hashicorp/vault\", \"2.0.3");
        appHost.Should().Contain("WithVolume(\"vault-data\", \"/vault/file\")");
        appHost.Should().NotContain("VAULT_DEV_");

        compose.Should().Contain("hashicorp/vault:2.0.3");
        compose.Should().Contain("vault-data:/vault/file");
        compose.Should().NotContain("VAULT_DEV_");

        vaultConfig.Should().Contain("storage \"file\"");
        vaultConfig.Should().Contain("path = \"/vault/file/data\"");
    }

    [Fact]
    public void LocalVaultToken_WhenBootstrapped_IsLimitedToAiProxySecrets()
    {
        var bootstrap = ReadFixture("src", "AiProxy.AppHost", "vault", "bootstrap.sh");
        var policy = ReadFixture("src", "AiProxy.AppHost", "vault", "ai-proxy-policy.hcl");

        bootstrap.Should().Contain("-policy=ai-proxy-local");
        bootstrap.Should().NotContain("-policy=root");
        bootstrap.Should().Contain("vault token revoke -self");
        policy.Should().Contain("secret/data/ai-proxy/*");
        policy.Should().Contain("secret/metadata/ai-proxy/*");
    }

    private static string ReadFixture(params string[] pathParts) =>
        File.ReadAllText(Path.Combine([FindRepositoryRoot(), .. pathParts]));

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AiProxy.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}