using AIProxyPlus.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace AIProxyPlus.Infrastructure.Persistence;

public sealed class AIProxyPlusDbContext(DbContextOptions<AIProxyPlusDbContext> options) : DbContext(options)
{
    public DbSet<Model> Models => Set<Model>();

    public DbSet<Endpoint> Endpoints => Set<Endpoint>();

    public DbSet<ApiRoute> ApiRoutes => Set<ApiRoute>();

    public DbSet<ModelEndpoint> ModelEndpoints => Set<ModelEndpoint>();

    public DbSet<Consumer> Consumers => Set<Consumer>();

    public DbSet<UsageRecord> UsageRecords => Set<UsageRecord>();

    public DbSet<SessionAffinityEntry> SessionAffinityEntries => Set<SessionAffinityEntry>();

    public DbSet<RealtimeTicket> RealtimeTickets => Set<RealtimeTicket>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AIProxyPlusDbContext).Assembly);
    }
}
