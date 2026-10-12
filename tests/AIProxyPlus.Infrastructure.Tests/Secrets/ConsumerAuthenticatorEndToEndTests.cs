using AIProxyPlus.Application.Consumers;
using AIProxyPlus.Domain.Entities;
using AIProxyPlus.Infrastructure.Repositories;
using AIProxyPlus.Infrastructure.Secrets;
using AIProxyPlus.Infrastructure.Tests.Persistence;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace AIProxyPlus.Infrastructure.Tests.Secrets;

/// <summary>End-to-end: real Postgres-backed <see cref="ConsumerRepository"/> + real Vault-backed <see cref="ApiKeyHasher"/>.</summary>
[Collection(nameof(PostgresCollection))]
public class ConsumerAuthenticatorEndToEndTests(PostgresContainerFixture postgres, VaultContainerFixture vault)
    : IClassFixture<VaultContainerFixture>
{
    private async Task<(ConsumerAuthenticator Authenticator, ApiKeyHasher Hasher, ConsumerRepository Repository)> CreateSutCoreAsync(
        AIProxyPlus.Infrastructure.Persistence.AIProxyPlusDbContext dbContext)
    {
        var vaultOptions = new VaultOptions
        {
            Address = vault.Address,
            Token = vault.Token,
            PathPrefix = $"aiproxyplus-test-{Guid.NewGuid():N}",
        };
        var secretResolver = new VaultSecretResolver(
            VaultClientFactory.Create(vaultOptions), vaultOptions, Substitute.For<ILogger<VaultSecretResolver>>());
        var hasher = await ApiKeyHasher.CreateAsync(secretResolver);
        var repository = new ConsumerRepository(dbContext);
        return (new ConsumerAuthenticator(repository, hasher), hasher, repository);
    }

    [Fact]
    public async Task AuthenticateAsync_WithValidEnabledConsumerKey_ReturnsConsumer()
    {
        await using var dbContext = postgres.CreateDbContext();
        var (authenticator, hasher, repository) = await CreateSutCoreAsync(dbContext);
        const string plaintextKey = "aip_test-key-1";
        var consumer = Consumer.Create(Guid.NewGuid(), $"team-{Guid.NewGuid():N}", hasher.Hash(plaintextKey));
        await repository.AddAsync(consumer);

        var result = await authenticator.AuthenticateAsync(plaintextKey);

        result.Should().NotBeNull();
        result!.Name.Should().Be(consumer.Name);
    }

    [Fact]
    public async Task AuthenticateAsync_WithUnknownKey_ReturnsNull()
    {
        await using var dbContext = postgres.CreateDbContext();
        var (authenticator, _, _) = await CreateSutCoreAsync(dbContext);

        var result = await authenticator.AuthenticateAsync("aip_never-registered");

        result.Should().BeNull();
    }

    [Fact]
    public async Task AuthenticateAsync_WithRevokedConsumerKey_ReturnsNull()
    {
        await using var dbContext = postgres.CreateDbContext();
        var (authenticator, hasher, repository) = await CreateSutCoreAsync(dbContext);
        const string plaintextKey = "aip_test-key-2";
        var consumer = Consumer.Create(Guid.NewGuid(), $"team-{Guid.NewGuid():N}", hasher.Hash(plaintextKey));
        consumer.Revoke();
        await repository.AddAsync(consumer);

        var result = await authenticator.AuthenticateAsync(plaintextKey);

        result.Should().BeNull();
    }
}
