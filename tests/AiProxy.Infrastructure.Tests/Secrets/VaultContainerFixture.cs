using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

namespace AiProxy.Infrastructure.Tests.Secrets;

/// <summary>Dev-mode HashiCorp Vault container (root-token auth) shared across the collection.</summary>
public sealed class VaultContainerFixture : IAsyncLifetime
{
    private const string RootToken = "test-root-token";
    private const int VaultPort = 8200;

    private readonly IContainer _container = new ContainerBuilder()
        .WithImage("hashicorp/vault:1.15")
        .WithEnvironment("VAULT_DEV_ROOT_TOKEN_ID", RootToken)
        .WithEnvironment("VAULT_DEV_LISTEN_ADDRESS", $"0.0.0.0:{VaultPort}")
        .WithPortBinding(VaultPort, true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(VaultPort).ForPath("/v1/sys/health")))
        .Build();

    public string Token => RootToken;

    public string Address => $"http://{_container.Hostname}:{_container.GetMappedPublicPort(VaultPort)}";

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}

[CollectionDefinition(nameof(VaultCollection))]
public sealed class VaultCollection : ICollectionFixture<VaultContainerFixture>;
