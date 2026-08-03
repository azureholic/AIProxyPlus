using AiProxy.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;

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

        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            SELECT table_name, column_name
            FROM information_schema.columns
            WHERE table_schema = 'public'
              AND table_name IN ('models', 'model_endpoints')
            """,
            connection);
        await using var reader = await command.ExecuteReaderAsync();
        var columns = new HashSet<(string Table, string Column)>();
        while (await reader.ReadAsync())
        {
            columns.Add((reader.GetString(0), reader.GetString(1)));
        }

        columns.Should().Contain(("models", "Name"));
        columns.Should().Contain(("model_endpoints", "ModelId"));
        columns.Should().Contain(("model_endpoints", "EndpointId"));
    }
}
