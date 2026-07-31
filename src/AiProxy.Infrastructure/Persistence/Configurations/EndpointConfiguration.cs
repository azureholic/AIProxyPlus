using AiProxy.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AiProxy.Infrastructure.Persistence.Configurations;

public sealed class EndpointConfiguration : IEntityTypeConfiguration<Endpoint>
{
    public void Configure(EntityTypeBuilder<Endpoint> builder)
    {
        builder.ToTable("endpoints");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.BaseUrl).IsRequired().HasMaxLength(2048);
        builder.Property(e => e.ApiFamily).HasConversion<string>().HasMaxLength(50);
        builder.Property(e => e.AuthMode).HasConversion<string>().HasMaxLength(50);
        builder.Property(e => e.Health).HasConversion<string>().HasMaxLength(50);
        builder.Property(e => e.SecretRef).HasMaxLength(500);
        builder.Property(e => e.ManagedIdentityScope).HasMaxLength(500);

        // Defense in depth: the domain aggregate already enforces this, but a unique index protects
        // against concurrent writers from multiple proxy instances racing past the in-memory check.
        builder.HasIndex(e => new { e.ModelDeploymentId, e.Priority }).IsUnique();
    }
}
