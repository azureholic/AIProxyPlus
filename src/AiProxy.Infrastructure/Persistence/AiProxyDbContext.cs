using AiProxy.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace AiProxy.Infrastructure.Persistence;

public sealed class AiProxyDbContext(DbContextOptions<AiProxyDbContext> options) : DbContext(options)
{
    public DbSet<ModelDeployment> ModelDeployments => Set<ModelDeployment>();

    public DbSet<Consumer> Consumers => Set<Consumer>();

    public DbSet<UsageRecord> UsageRecords => Set<UsageRecord>();

    public DbSet<SessionAffinityEntry> SessionAffinityEntries => Set<SessionAffinityEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AiProxyDbContext).Assembly);
    }
}
