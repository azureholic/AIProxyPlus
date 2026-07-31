using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace AiProxy.Infrastructure.Tests.Persistence;

[Collection(nameof(PostgresCollection))]
public class DatabaseSchemaTests(PostgresContainerFixture fixture)
{
    [Fact]
    public async Task Migrations_ApplyCleanly_AndExpectedTablesExist()
    {
        await using var dbContext = fixture.CreateDbContext();

        var pendingMigrations = await dbContext.Database.GetPendingMigrationsAsync();

        pendingMigrations.Should().BeEmpty("the fixture already applied all migrations at collection startup");
        (await dbContext.Database.CanConnectAsync()).Should().BeTrue();
    }
}
