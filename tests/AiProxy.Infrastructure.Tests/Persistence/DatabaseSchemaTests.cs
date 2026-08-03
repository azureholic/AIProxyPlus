using AiProxy.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
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

        await reader.CloseAsync();
        command.CommandText =
            "SELECT \"Name\", \"Provider\", \"PathPrefix\" FROM api_routes WHERE \"IsBuiltIn\" = TRUE ORDER BY \"Name\"";
        await using var routesReader = await command.ExecuteReaderAsync();
        var routes = new List<(string Name, string Provider, string PathPrefix)>();
        while (await routesReader.ReadAsync())
        {
            routes.Add((routesReader.GetString(0), routesReader.GetString(1), routesReader.GetString(2)));
        }

        routes.Should().BeEquivalentTo([
            ("Foundry OpenAI", "OpenAI", "/openai/v1"),
            ("Microsoft AI", "MicrosoftAI", "/mai/v1"),
            ("Anthropic", "Anthropic", "/anthropic/v1"),
            ("Native OpenAI", "OpenAI", "/v1"),
        ]);
    }

    [Fact]
    public async Task ReusableApiRoutesMigration_LegacyData_UpgradesAndDowngradesWithoutLosingAssignments()
    {
        var connectionString = await fixture.CreateIsolatedDatabaseAsync();
        try
        {
            await using var dbContext = PostgresContainerFixture.CreateDbContext(connectionString);
            var migrator = dbContext.GetService<IMigrator>();
            await migrator.MigrateAsync("20260803130348_InitialModelSchema");

            var modelId = Guid.NewGuid();
            var endpointId = Guid.NewGuid();
            var usageId = Guid.NewGuid();
            var consumerId = Guid.NewGuid();
            await SeedLegacyDataAsync(connectionString, modelId, endpointId, usageId, consumerId);

            await migrator.MigrateAsync();

            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(
                """
                SELECT route."Id", route."Name", assignment."Id", affinity."ModelEndpointId", usage."Provider"
                FROM model_endpoints AS assignment
                JOIN api_routes AS route ON route."Id" = assignment."ApiRouteId"
                JOIN session_affinity AS affinity ON affinity."ModelEndpointId" = assignment."Id"
                JOIN usage_records AS usage ON usage."Id" = @usageId
                WHERE assignment."ModelId" = @modelId AND assignment."EndpointId" = @endpointId
                """,
                connection);
            command.Parameters.AddWithValue("usageId", usageId);
            command.Parameters.AddWithValue("modelId", modelId);
            command.Parameters.AddWithValue("endpointId", endpointId);
            await using var reader = await command.ExecuteReaderAsync();
            (await reader.ReadAsync()).Should().BeTrue();
            reader.GetGuid(0).Should().Be(Guid.Parse("7d746f5a-9a9b-4cf4-91e8-5fa231ae9c01"));
            reader.GetString(1).Should().Be("Foundry OpenAI");
            reader.GetGuid(2).Should().Be(reader.GetGuid(3));
            reader.GetString(4).Should().Be("OpenAI");
            await reader.CloseAsync();

            await migrator.MigrateAsync("20260803130348_InitialModelSchema");

            command.CommandText =
                """
                SELECT endpoint."ApiFamily", COUNT(assignment.*), affinity."EndpointId"
                FROM endpoints AS endpoint
                JOIN model_endpoints AS assignment ON assignment."EndpointId" = endpoint."Id"
                JOIN session_affinity AS affinity ON affinity."EndpointId" = endpoint."Id"
                WHERE endpoint."Id" = @endpointId
                GROUP BY endpoint."ApiFamily", affinity."EndpointId"
                """;
            await using var downgradedReader = await command.ExecuteReaderAsync();
            (await downgradedReader.ReadAsync()).Should().BeTrue();
            downgradedReader.GetString(0).Should().Be("AzureOpenAI");
            downgradedReader.GetInt64(1).Should().Be(1);
            downgradedReader.GetGuid(2).Should().Be(endpointId);
        }
        finally
        {
            await fixture.DropIsolatedDatabaseAsync(connectionString);
        }
    }

    private static async Task SeedLegacyDataAsync(
        string connectionString,
        Guid modelId,
        Guid endpointId,
        Guid usageId,
        Guid consumerId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO models ("Id", "Name", "Strategy", "CreatedAt", "UpdatedAt")
            VALUES (@modelId, 'legacy-model', 'Priority', NOW(), NOW());

            INSERT INTO endpoints ("Id", "Name", "BaseUrl", "ApiFamily", "AuthMode", "SecretRef", "TimeoutSeconds", "MaxRetries")
            VALUES (@endpointId, 'legacy-endpoint', 'https://legacy.example.com', 'AzureOpenAI', 'ApiKey', 'legacy-secret', 30, 2);

            INSERT INTO model_endpoints ("ModelId", "EndpointId", "Priority", "Enabled", "Health")
            VALUES (@modelId, @endpointId, 1, TRUE, 'Healthy');

            INSERT INTO session_affinity ("SessionId", "ModelId", "EndpointId", "LastUsedAt", "ExpiresAt")
            VALUES ('legacy-response', @modelId, @endpointId, NOW(), NOW() + INTERVAL '1 hour');

            INSERT INTO usage_records (
                "Id", "Timestamp", "ModelName", "ConsumerId", "ConsumerName", "EndpointId", "ApiFamily",
                "OperationType", "RequestId", "Streaming", "HttpStatusCode", "DurationMs")
            VALUES (
                @usageId, NOW(), 'legacy-model', @consumerId, 'legacy-consumer', @endpointId, 'AzureOpenAI',
                'ChatCompletion', 'legacy-request', FALSE, 200, 10);
            """,
            connection);
        command.Parameters.AddWithValue("modelId", modelId);
        command.Parameters.AddWithValue("endpointId", endpointId);
        command.Parameters.AddWithValue("usageId", usageId);
        command.Parameters.AddWithValue("consumerId", consumerId);
        await command.ExecuteNonQueryAsync();
    }
}
