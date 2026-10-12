using AIProxyPlus.Domain.Entities;
using AIProxyPlus.Infrastructure.Repositories;
using FluentAssertions;

namespace AIProxyPlus.Infrastructure.Tests.Persistence;

[Collection(nameof(PostgresCollection))]
public class SessionAffinityStoreTests(PostgresContainerFixture fixture)
{
    [Fact]
    public async Task SaveAsync_Then_GetAsync_RoundTrips()
    {
        var sessionId = $"session-{Guid.NewGuid():N}";
        var entry = SessionAffinityEntry.Create(
            sessionId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), TimeSpan.FromMinutes(30));
        await using var writeContext = fixture.CreateDbContext();
        await new SessionAffinityStore(writeContext).SaveAsync(entry);

        await using var readContext = fixture.CreateDbContext();
        var loaded = await new SessionAffinityStore(readContext).GetAsync(sessionId);

        loaded.Should().NotBeNull();
        loaded!.EndpointId.Should().Be(entry.EndpointId);
    }

    [Fact]
    public async Task GetAsync_ForUnknownSession_ReturnsNull()
    {
        await using var dbContext = fixture.CreateDbContext();

        var result = await new SessionAffinityStore(dbContext).GetAsync($"missing-{Guid.NewGuid():N}");

        result.Should().BeNull();
    }

    [Fact]
    public async Task SaveAsync_CalledTwiceForSameSession_UpdatesExpiryInPlace()
    {
        var sessionId = $"session-{Guid.NewGuid():N}";
        var modelId = Guid.NewGuid();
        var modelEndpointId = Guid.NewGuid();
        var endpointId = Guid.NewGuid();
        var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        await using var writeContext1 = fixture.CreateDbContext();
        var entry = SessionAffinityEntry.Create(
            sessionId, modelId, modelEndpointId, endpointId, TimeSpan.FromMinutes(30), now);
        await new SessionAffinityStore(writeContext1).SaveAsync(entry);

        entry.Touch(TimeSpan.FromMinutes(30), now.AddMinutes(10));
        await using var writeContext2 = fixture.CreateDbContext();
        await new SessionAffinityStore(writeContext2).SaveAsync(entry);

        await using var readContext = fixture.CreateDbContext();
        var loaded = await new SessionAffinityStore(readContext).GetAsync(sessionId);

        loaded!.ExpiresAt.Should().Be(now.AddMinutes(40));
    }

    [Fact]
    public async Task RemoveAsync_DeletesEntry()
    {
        var sessionId = $"session-{Guid.NewGuid():N}";
        await using var writeContext = fixture.CreateDbContext();
        await new SessionAffinityStore(writeContext).SaveAsync(
            SessionAffinityEntry.Create(
                sessionId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), TimeSpan.FromMinutes(30)));

        await using var removeContext = fixture.CreateDbContext();
        await new SessionAffinityStore(removeContext).RemoveAsync(sessionId);

        await using var readContext = fixture.CreateDbContext();
        (await new SessionAffinityStore(readContext).GetAsync(sessionId)).Should().BeNull();
    }

    [Fact]
    public async Task RemoveAsync_ForUnknownSession_DoesNotThrow()
    {
        await using var dbContext = fixture.CreateDbContext();

        var act = () => new SessionAffinityStore(dbContext).RemoveAsync($"missing-{Guid.NewGuid():N}");

        await act.Should().NotThrowAsync();
    }
}
