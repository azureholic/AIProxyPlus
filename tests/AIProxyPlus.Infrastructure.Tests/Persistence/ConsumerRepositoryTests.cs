using AIProxyPlus.Domain.Entities;
using AIProxyPlus.Domain.Enums;
using AIProxyPlus.Infrastructure.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace AIProxyPlus.Infrastructure.Tests.Persistence;

[Collection(nameof(PostgresCollection))]
public class ConsumerRepositoryTests(PostgresContainerFixture fixture)
{
    private static Consumer NewConsumer(string? name = null, string? hash = null) =>
        Consumer.Create(Guid.NewGuid(), name ?? $"team-{Guid.NewGuid():N}", hash ?? $"hash-{Guid.NewGuid():N}");

    [Fact]
    public async Task AddAsync_Then_GetByNameAsync_RoundTrips()
    {
        await using var writeContext = fixture.CreateDbContext();
        var consumer = NewConsumer();
        await new ConsumerRepository(writeContext).AddAsync(consumer);

        await using var readContext = fixture.CreateDbContext();
        var loaded = await new ConsumerRepository(readContext).GetByNameAsync(consumer.Name);

        loaded.Should().NotBeNull();
        loaded!.ApiKeyHash.Should().Be(consumer.ApiKeyHash);
    }

    [Fact]
    public async Task GetByApiKeyHashAsync_FindsMatchingConsumer()
    {
        await using var writeContext = fixture.CreateDbContext();
        var consumer = NewConsumer();
        await new ConsumerRepository(writeContext).AddAsync(consumer);

        await using var readContext = fixture.CreateDbContext();
        var loaded = await new ConsumerRepository(readContext).GetByApiKeyHashAsync(consumer.ApiKeyHash!);

        loaded.Should().NotBeNull();
        loaded!.Name.Should().Be(consumer.Name);
    }

    [Fact]
    public async Task GetByApiKeyHashAsync_FindsConsumerBySecondaryKey()
    {
        var secondaryHash = $"hash-2-{Guid.NewGuid():N}";
        var consumer = Consumer.CreateApiKey(
            Guid.NewGuid(), $"team-{Guid.NewGuid():N}", $"hash-1-{Guid.NewGuid():N}", secondaryHash);
        await using var writeContext = fixture.CreateDbContext();
        await new ConsumerRepository(writeContext).AddAsync(consumer);

        await using var readContext = fixture.CreateDbContext();
        var loaded = await new ConsumerRepository(readContext).GetByApiKeyHashAsync(secondaryHash);

        loaded.Should().NotBeNull();
        loaded!.Id.Should().Be(consumer.Id);
    }

    [Fact]
    public async Task GetByApiKeyHashAsync_ForUnknownHash_ReturnsNull()
    {
        await using var dbContext = fixture.CreateDbContext();

        var result = await new ConsumerRepository(dbContext).GetByApiKeyHashAsync($"missing-{Guid.NewGuid():N}");

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetByOidcIdentityAsync_FindsMatchingConsumer()
    {
        var consumer = Consumer.CreateOidc(
            Guid.NewGuid(),
            $"user-{Guid.NewGuid():N}",
            "https://identity.example.com/realms/aiproxyplus",
            $"subject-{Guid.NewGuid():N}");

        await using var writeContext = fixture.CreateDbContext();
        await new ConsumerRepository(writeContext).AddAsync(consumer);

        await using var readContext = fixture.CreateDbContext();
        var loaded = await new ConsumerRepository(readContext).GetByOidcIdentityAsync(
            consumer.OidcIssuer!,
            consumer.OidcSubject!);

        loaded.Should().NotBeNull();
        loaded!.Name.Should().Be(consumer.Name);
        loaded.ApiKeyHash.Should().BeNull();
    }

    [Fact]
    public async Task AddAsync_WithDuplicateOidcIdentity_ThrowsDueToUniqueIndex()
    {
        var issuer = "https://identity.example.com/realms/aiproxyplus";
        var subject = $"subject-{Guid.NewGuid():N}";

        await using var context1 = fixture.CreateDbContext();
        await new ConsumerRepository(context1).AddAsync(
            Consumer.CreateOidc(Guid.NewGuid(), $"user-{Guid.NewGuid():N}", issuer, subject));

        await using var context2 = fixture.CreateDbContext();
        var act = () => new ConsumerRepository(context2).AddAsync(
            Consumer.CreateOidc(Guid.NewGuid(), $"user-{Guid.NewGuid():N}", issuer, subject));

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task GetByManagedIdentityAsync_FindsMatchingConsumer()
    {
        var consumer = Consumer.CreateManagedIdentity(
            Guid.NewGuid(),
            $"worker-{Guid.NewGuid():N}",
            Guid.NewGuid(),
            Guid.NewGuid());

        await using var writeContext = fixture.CreateDbContext();
        await new ConsumerRepository(writeContext).AddAsync(consumer);

        await using var readContext = fixture.CreateDbContext();
        var loaded = await new ConsumerRepository(readContext).GetByManagedIdentityAsync(
            consumer.ManagedIdentityTenantId!.Value,
            consumer.ManagedIdentityObjectId!.Value);

        loaded.Should().NotBeNull();
        loaded!.Id.Should().Be(consumer.Id);
        loaded.ApiKeyHash.Should().BeNull();
    }

    [Fact]
    public async Task AddAsync_WithDuplicateName_ThrowsDueToUniqueIndex()
    {
        var name = $"team-{Guid.NewGuid():N}";

        await using var context1 = fixture.CreateDbContext();
        await new ConsumerRepository(context1).AddAsync(NewConsumer(name));

        await using var context2 = fixture.CreateDbContext();
        var act = () => new ConsumerRepository(context2).AddAsync(NewConsumer(name));

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task UpdateAsync_PersistsRotatedKeyAndRevocation()
    {
        await using var writeContext = fixture.CreateDbContext();
        var consumer = NewConsumer();
        var repository = new ConsumerRepository(writeContext);
        await repository.AddAsync(consumer);

        consumer.RotateKey(ApiKeySlot.Primary, $"new-hash-{Guid.NewGuid():N}");
        consumer.Revoke();
        await repository.UpdateAsync(consumer);

        await using var readContext = fixture.CreateDbContext();
        var loaded = await new ConsumerRepository(readContext).GetByNameAsync(consumer.Name);

        loaded!.ApiKeyHash.Should().Be(consumer.ApiKeyHash);
        loaded.Enabled.Should().BeFalse();
        loaded.RevokedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task UpdateAsync_PersistsAllowedModels()
    {
        var model = Model.Create(Guid.NewGuid(), $"model-{Guid.NewGuid():N}");
        await using (var modelContext = fixture.CreateDbContext())
        {
            await new ModelRepository(modelContext).AddAsync(model);
        }

        var consumer = NewConsumer();
        await using (var writeContext = fixture.CreateDbContext())
        {
            var repository = new ConsumerRepository(writeContext);
            await repository.AddAsync(consumer);
            consumer.AllowModel(model.Id);
            await repository.UpdateAsync(consumer);
        }

        await using var readContext = fixture.CreateDbContext();
        var loaded = await new ConsumerRepository(readContext).GetByNameAsync(consumer.Name);

        loaded!.AllowedModelIds.Should().ContainSingle().Which.Should().Be(model.Id);
        loaded.CanUseModel(model.Id).Should().BeTrue();
        loaded.CanUseModel(Guid.NewGuid()).Should().BeFalse();
    }
}
