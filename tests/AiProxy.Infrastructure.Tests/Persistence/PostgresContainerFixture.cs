using AiProxy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace AiProxy.Infrastructure.Tests.Persistence;

/// <summary>
/// One Postgres container shared across the whole test collection (fast) with migrations applied once;
/// each test gets its own <see cref="AiProxyDbContext"/> instance to avoid change-tracker state leaking
/// between tests, and uses Guid-suffixed names to avoid colliding with data from other tests.
/// </summary>
public sealed class PostgresContainerFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("aiproxy")
        .WithUsername("aiproxy")
        .WithPassword("aiproxy")
        .Build();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        await using var dbContext = CreateDbContext();
        await dbContext.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    public string ConnectionString => _container.GetConnectionString();

    public async Task<string> CreateIsolatedDatabaseAsync()
    {
        var databaseName = $"aiproxy_{Guid.NewGuid():N}";
        var adminConnectionString = new NpgsqlConnectionStringBuilder(ConnectionString) { Database = "postgres" }.ConnectionString;
        await using var connection = new NpgsqlConnection(adminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\"", connection);
        await command.ExecuteNonQueryAsync();

        return new NpgsqlConnectionStringBuilder(ConnectionString) { Database = databaseName }.ConnectionString;
    }

    public async Task DropIsolatedDatabaseAsync(string connectionString)
    {
        var databaseName = new NpgsqlConnectionStringBuilder(connectionString).Database;
        var adminConnectionString = new NpgsqlConnectionStringBuilder(ConnectionString) { Database = "postgres" }.ConnectionString;
        NpgsqlConnection.ClearAllPools();
        await using var connection = new NpgsqlConnection(adminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"DROP DATABASE \"{databaseName}\" WITH (FORCE)", connection);
        await command.ExecuteNonQueryAsync();
    }

    public AiProxyDbContext CreateDbContext()
        => CreateDbContext(ConnectionString);

    public static AiProxyDbContext CreateDbContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<AiProxyDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        return new AiProxyDbContext(options);
    }
}

[CollectionDefinition(nameof(PostgresCollection))]
public sealed class PostgresCollection : ICollectionFixture<PostgresContainerFixture>;
