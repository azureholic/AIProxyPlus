using System.Data.Common;
using AIProxyPlus.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace AIProxyPlus.Infrastructure.Tests.Persistence;

public sealed class DatabaseMigrationExtensionsTests
{
    [Theory]
    [InlineData(null)]
    [InlineData(7)]
    public async Task MigrateWithLockAsync_LockAcquired_RestoresTimeoutBeforeMigrating(int? timeout)
    {
        var commands = new MigrationInterceptor();
        var migrator = Substitute.For<IMigrator>();
        using var services = CreateServices(migrator);
        await using var context = CreateContext(services, commands, timeout);
        int? migrationTimeout = -1;
        migrator.MigrateAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                migrationTimeout = context.Database.GetCommandTimeout();
                return Task.CompletedTask;
            });

        await context.MigrateWithLockAsync();

        commands.Commands.Should().Equal(
            ("SELECT pg_advisory_lock(4704379600213924176)", 600),
            ("SELECT pg_advisory_unlock(4704379600213924176)", timeout ?? 30));
        migrationTimeout.Should().Be(timeout);
        context.Database.GetCommandTimeout().Should().Be(timeout);
    }

    [Theory]
    [InlineData("lock", null)]
    [InlineData("lock", 7)]
    [InlineData("migration", null)]
    [InlineData("migration", 7)]
    public async Task MigrateWithLockAsync_OperationFails_RestoresTimeoutAndReleasesOnlyAcquiredLock(
        string failure, int? timeout)
    {
        var commands = new MigrationInterceptor(failure);
        var migrator = Substitute.For<IMigrator>();
        if (failure == "migration")
        {
            migrator.MigrateAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromException(new InvalidOperationException("migration failed")));
        }
        using var services = CreateServices(migrator);
        await using var context = CreateContext(services, commands, timeout);

        var act = () => context.MigrateWithLockAsync();

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage($"{failure} failed");
        context.Database.GetCommandTimeout().Should().Be(timeout);
        commands.Commands.Should().HaveCount(failure == "lock" ? 1 : 2);
        commands.Commands[0].Timeout.Should().Be(600);
        if (failure == "migration")
        {
            commands.Commands[1].Should().Be(
                ("SELECT pg_advisory_unlock(4704379600213924176)", timeout ?? 30));
        }
        await migrator.Received(failure == "lock" ? 0 : 1)
            .MigrateAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    private static ServiceProvider CreateServices(IMigrator migrator) =>
        new ServiceCollection().AddEntityFrameworkNpgsql().AddScoped<IMigrator>(_ => migrator)
            .BuildServiceProvider();

    private static AIProxyPlusDbContext CreateContext(
        ServiceProvider services, MigrationInterceptor interceptor, int? timeout) =>
        new(new DbContextOptionsBuilder<AIProxyPlusDbContext>()
            .UseNpgsql("Host=localhost;Database=migration-tests", options => options.CommandTimeout(timeout))
            .UseInternalServiceProvider(services)
            .AddInterceptors(interceptor)
            .Options);

    private sealed class MigrationInterceptor(string? failure = null) : DbConnectionInterceptor, IDbCommandInterceptor
    {
        public List<(string Sql, int Timeout)> Commands { get; } = [];

        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(
            DbConnection connection, ConnectionEventData eventData, InterceptionResult result,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(InterceptionResult.Suppress());

        public ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            Commands.Add((command.CommandText, command.CommandTimeout));
            var operation = command.CommandText.Contains("pg_advisory_unlock") ? "unlock" : "lock";
            if (failure == operation)
            {
                throw new InvalidOperationException($"{operation} failed");
            }
            return ValueTask.FromResult(InterceptionResult<int>.SuppressWithResult(1));
        }
    }
}
