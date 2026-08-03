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
    }

    [Fact]
    public async Task ReusableEndpointsMigration_ExistingEndpoint_PreservesIdentitySecretAndAssignmentState()
    {
        var databaseName = $"migration_{Guid.NewGuid():N}";
        var connectionStringBuilder = new NpgsqlConnectionStringBuilder(fixture.ConnectionString);

        await using (var adminConnection = new NpgsqlConnection(fixture.ConnectionString))
        {
            await adminConnection.OpenAsync();
            await using var createDatabase = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\"", adminConnection);
            await createDatabase.ExecuteNonQueryAsync();
        }

        connectionStringBuilder.Database = databaseName;

        try
        {
            var options = new DbContextOptionsBuilder<AiProxyDbContext>()
                .UseNpgsql(connectionStringBuilder.ConnectionString)
                .Options;
            await using var dbContext = new AiProxyDbContext(options);
            var migrator = dbContext.Database.GetService<IMigrator>();

            await migrator.MigrateAsync("20260803105851_AddManagedIdentityTenantId");

            var deploymentId = Guid.NewGuid();
            var endpointId = Guid.NewGuid();
            var secretRef = $"endpoints/{endpointId}";
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO model_deployments ("Id", "Name", "Strategy", "CreatedAt", "UpdatedAt")
                VALUES ({deploymentId}, 'existing-model', 'Priority', NOW(), NOW());

                INSERT INTO endpoints
                    ("Id", "ModelDeploymentId", "BaseUrl", "ApiFamily", "Priority", "AuthMode", "SecretRef",
                     "ManagedIdentityScope", "ManagedIdentityTenantId", "Enabled", "TimeoutSeconds", "MaxRetries", "Health")
                VALUES
                    ({endpointId}, {deploymentId}, 'https://example.openai.azure.com', 'AzureOpenAI', 7, 'ApiKey',
                     {secretRef}, NULL, NULL, FALSE, 42, 3, 'Unhealthy');
                """);

            await migrator.MigrateAsync();

            await using var verificationConnection = new NpgsqlConnection(connectionStringBuilder.ConnectionString);
            await verificationConnection.OpenAsync();
            await using var command = new NpgsqlCommand(
                """
                SELECT e."Id", e."SecretRef", mde."ModelDeploymentId", mde."Priority", mde."Enabled", mde."Health"
                FROM endpoints e
                JOIN model_deployment_endpoints mde ON mde."EndpointId" = e."Id"
                WHERE e."Id" = @endpointId
                """,
                verificationConnection);
            command.Parameters.AddWithValue("endpointId", endpointId);
            await using var reader = await command.ExecuteReaderAsync();

            (await reader.ReadAsync()).Should().BeTrue();
            reader.GetGuid(0).Should().Be(endpointId);
            reader.GetString(1).Should().Be(secretRef);
            reader.GetGuid(2).Should().Be(deploymentId);
            reader.GetInt32(3).Should().Be(7);
            reader.GetBoolean(4).Should().BeFalse();
            reader.GetString(5).Should().Be("Unhealthy");
            (await reader.ReadAsync()).Should().BeFalse();
        }
        finally
        {
            await using var adminConnection = new NpgsqlConnection(fixture.ConnectionString);
            await adminConnection.OpenAsync();
            await using var dropDatabase = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE)", adminConnection);
            await dropDatabase.ExecuteNonQueryAsync();
        }
    }
}
