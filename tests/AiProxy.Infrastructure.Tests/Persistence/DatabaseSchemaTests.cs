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
    public async Task Migrations_ApplyCleanly_ViaADistinctMigrationsConnectionString()
    {
        // Iteration 12 least-privilege support (architecture.md §13): Program.cs applies migrations through a
        // dedicated AiProxyDbContext built from a separate connection string when one is configured, rather
        // than the regular DI-registered context used for runtime queries. This proves that code path — a
        // context built directly from a raw connection string — still applies every migration correctly.
        var migrationsConnectionString = await fixture.CreateIsolatedDatabaseAsync();
        try
        {
            var options = new DbContextOptionsBuilder<AiProxyDbContext>().UseNpgsql(migrationsConnectionString).Options;
            await using var migrationsContext = new AiProxyDbContext(options);

            await migrationsContext.Database.MigrateAsync();

            (await migrationsContext.Database.GetAppliedMigrationsAsync()).Should().NotBeEmpty();
            (await migrationsContext.Database.GetPendingMigrationsAsync()).Should().BeEmpty();
            (await migrationsContext.Database.CanConnectAsync()).Should().BeTrue();
        }
        finally
        {
            await fixture.DropIsolatedDatabaseAsync(migrationsConnectionString);
        }
    }

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
            ("Third Party OpenAI", "OpenAI", "/openai/v1"),
            ("Foundry Microsoft AI", "MicrosoftAI", "/mai/v1"),
            ("Third Party Anthropic", "Anthropic", "/anthropic/v1"),
            ("Native Anthropic", "Anthropic", "/v1"),
            ("Native OpenAI", "OpenAI", "/v1"),
        ]);
    }

    [Fact]
    public async Task EndpointCredentialConstraints_RejectIncompleteOrInapplicableSlotState()
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        var invalidRows = new[]
        {
            """
            INSERT INTO endpoints (
                "Id", "Name", "BaseUrl", "AuthMode", "PrimarySecretRef", "TimeoutSeconds", "MaxRetries")
            VALUES (@id, @name, 'https://api.example.com/', 'ApiKey', 'endpoints/test/primary', 30, 2)
            """,
            """
            INSERT INTO endpoints (
                "Id", "Name", "BaseUrl", "AuthMode", "PrimarySecretRef", "ActiveCredentialSlot",
                "ManagedIdentityScope", "TimeoutSeconds", "MaxRetries")
            VALUES (
                @id, @name, 'https://api.example.com/', 'ManagedIdentity', 'endpoints/test/primary', 'Primary',
                'https://example.com/.default', 30, 2)
            """,
        };

        foreach (var sql in invalidRows)
        {
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("id", Guid.NewGuid());
            command.Parameters.AddWithValue("name", $"invalid-{Guid.NewGuid():N}");

            var act = () => command.ExecuteNonQueryAsync();

            (await act.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("23514");
        }
    }

    [Fact]
    public async Task ReclassifyAnthropicMessagesApiUsageMigration_BackfillsHistoricalRecords()
    {
        var connectionString = await fixture.CreateIsolatedDatabaseAsync();
        try
        {
            await using var dbContext = PostgresContainerFixture.CreateDbContext(connectionString);
            var migrator = dbContext.GetService<IMigrator>();
            await migrator.MigrateAsync("20260804100338_RenameFoundryProviderRoutes");

            var recordId = Guid.NewGuid();
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(
                """
                INSERT INTO usage_records (
                    "Id", "Timestamp", "ModelName", "ConsumerId", "ConsumerName", "EndpointId", "Provider",
                    "OperationType", "RequestId", "Streaming", "HttpStatusCode", "DurationMs")
                VALUES (
                    @recordId, NOW(), 'claude-haiku-4-5', @consumerId, 'legacy-consumer', @endpointId, 'Anthropic',
                    'ChatCompletion', 'legacy-anthropic-request', FALSE, 200, 10)
                """,
                connection);
            command.Parameters.AddWithValue("recordId", recordId);
            command.Parameters.AddWithValue("consumerId", Guid.NewGuid());
            command.Parameters.AddWithValue("endpointId", Guid.NewGuid());
            await command.ExecuteNonQueryAsync();

            await migrator.MigrateAsync();

            command.CommandText = "SELECT \"OperationType\" FROM usage_records WHERE \"Id\" = @recordId";
            (await command.ExecuteScalarAsync()).Should().Be("MessagesApi");

            await migrator.MigrateAsync("20260804100338_RenameFoundryProviderRoutes");

            (await command.ExecuteScalarAsync()).Should().Be("ChatCompletion");
        }
        finally
        {
            await fixture.DropIsolatedDatabaseAsync(connectionString);
        }
    }

    [Fact]
    public async Task AddEndpointNameToUsageRecordsMigration_BackfillsFromEndpointsTable()
    {
        var connectionString = await fixture.CreateIsolatedDatabaseAsync();
        try
        {
            await using var dbContext = PostgresContainerFixture.CreateDbContext(connectionString);
            var migrator = dbContext.GetService<IMigrator>();
            await migrator.MigrateAsync("20260804102121_ReclassifyAnthropicMessagesApiUsage");

            var endpointId = Guid.NewGuid();
            var recordId = Guid.NewGuid();
            var orphanedRecordId = Guid.NewGuid();
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(
                """
                INSERT INTO endpoints (
                    "Id", "Name", "BaseUrl", "AuthMode", "SecretRef", "TimeoutSeconds", "MaxRetries")
                VALUES (
                    @endpointId, 'prod-anthropic', 'https://api.anthropic.com/', 'ApiKey',
                    'endpoints/prod-anthropic', 30, 2);

                INSERT INTO usage_records (
                    "Id", "Timestamp", "ModelName", "ConsumerId", "ConsumerName", "EndpointId", "Provider",
                    "OperationType", "RequestId", "Streaming", "HttpStatusCode", "DurationMs")
                VALUES (
                    @recordId, NOW(), 'claude-haiku-4-5', @consumerId, 'legacy-consumer', @endpointId, 'Anthropic',
                    'MessagesApi', 'legacy-request', FALSE, 200, 10);

                INSERT INTO usage_records (
                    "Id", "Timestamp", "ModelName", "ConsumerId", "ConsumerName", "EndpointId", "Provider",
                    "OperationType", "RequestId", "Streaming", "HttpStatusCode", "DurationMs")
                VALUES (
                    @orphanedRecordId, NOW(), 'gpt-4o-prod', @consumerId, 'legacy-consumer', @orphanedEndpointId,
                    'OpenAI', 'ChatCompletion', 'orphaned-request', FALSE, 200, 10);
                """,
                connection);
            command.Parameters.AddWithValue("endpointId", endpointId);
            command.Parameters.AddWithValue("recordId", recordId);
            command.Parameters.AddWithValue("orphanedRecordId", orphanedRecordId);
            command.Parameters.AddWithValue("consumerId", Guid.NewGuid());
            command.Parameters.AddWithValue("orphanedEndpointId", Guid.NewGuid());
            await command.ExecuteNonQueryAsync();

            await migrator.MigrateAsync();

            command.CommandText = "SELECT \"EndpointName\" FROM usage_records WHERE \"Id\" = @recordId";
            (await command.ExecuteScalarAsync()).Should().Be("prod-anthropic");

            command.CommandText = "SELECT \"EndpointName\" FROM usage_records WHERE \"Id\" = @orphanedRecordId";
            (await command.ExecuteScalarAsync()).Should().Be("unknown");

            await migrator.MigrateAsync("20260804102121_ReclassifyAnthropicMessagesApiUsage");

            command.CommandText =
                "SELECT COUNT(*) FROM information_schema.columns " +
                "WHERE table_name = 'usage_records' AND column_name = 'EndpointName'";
            (await command.ExecuteScalarAsync()).Should().Be(0L);
        }
        finally
        {
            await fixture.DropIsolatedDatabaseAsync(connectionString);
        }
    }

    [Fact]
    public async Task RenameThirdPartyProviderRoutesMigration_RenamesOnlyUnmodifiedDefaultNames()
    {
        var connectionString = await fixture.CreateIsolatedDatabaseAsync();
        try
        {
            await using var dbContext = PostgresContainerFixture.CreateDbContext(connectionString);
            var migrator = dbContext.GetService<IMigrator>();
            await migrator.MigrateAsync("20260804104659_AddEndpointNameToUsageRecords");

            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(
                """
                UPDATE api_routes SET "Name" = 'My Anthropic'
                WHERE "Id" = '7d746f5a-9a9b-4cf4-91e8-5fa231ae9c03'
                """,
                connection);
            await command.ExecuteNonQueryAsync();

            await migrator.MigrateAsync();

            command.CommandText =
                "SELECT \"Name\" FROM api_routes WHERE \"Id\" = '7d746f5a-9a9b-4cf4-91e8-5fa231ae9c01'";
            (await command.ExecuteScalarAsync()).Should().Be("Third Party OpenAI");

            command.CommandText =
                "SELECT \"Name\" FROM api_routes WHERE \"Id\" = '7d746f5a-9a9b-4cf4-91e8-5fa231ae9c03'";
            (await command.ExecuteScalarAsync()).Should().Be("My Anthropic", "an administrator's own rename must not be overwritten");

            await migrator.MigrateAsync("20260804104659_AddEndpointNameToUsageRecords");

            command.CommandText =
                "SELECT \"Name\" FROM api_routes WHERE \"Id\" = '7d746f5a-9a9b-4cf4-91e8-5fa231ae9c01'";
            (await command.ExecuteScalarAsync()).Should().Be("Foundry OpenAI");
        }
        finally
        {
            await fixture.DropIsolatedDatabaseAsync(connectionString);
        }
    }

    [Fact]
    public async Task NativeAnthropicRouteMigration_PromotesExistingCustomRouteInPlace()
    {
        var connectionString = await fixture.CreateIsolatedDatabaseAsync();
        try
        {
            await using var dbContext = PostgresContainerFixture.CreateDbContext(connectionString);
            var migrator = dbContext.GetService<IMigrator>();
            await migrator.MigrateAsync("20260803142716_ReusableApiRoutes");

            var routeId = Guid.NewGuid();
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(
                """
                INSERT INTO api_routes ("Id", "Name", "Provider", "PathPrefix", "IsBuiltIn")
                VALUES (@routeId, 'Native Anthropic', 'Anthropic', '/v1', FALSE)
                """,
                connection);
            command.Parameters.AddWithValue("routeId", routeId);
            await command.ExecuteNonQueryAsync();

            await migrator.MigrateAsync();

            command.CommandText =
                "SELECT \"Id\", \"IsBuiltIn\" FROM api_routes WHERE \"Provider\" = 'Anthropic' AND \"PathPrefix\" = '/v1'";
            await using (var reader = await command.ExecuteReaderAsync())
            {
                (await reader.ReadAsync()).Should().BeTrue();
                reader.GetGuid(0).Should().Be(routeId);
                reader.GetBoolean(1).Should().BeTrue();
            }

            await migrator.MigrateAsync("20260803142716_ReusableApiRoutes");

            command.CommandText =
                "SELECT \"IsBuiltIn\" FROM api_routes WHERE \"Id\" = @routeId";
            (await command.ExecuteScalarAsync()).Should().Be(false);
        }
        finally
        {
            await fixture.DropIsolatedDatabaseAsync(connectionString);
        }
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
            reader.GetString(1).Should().Be("Third Party OpenAI");
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
