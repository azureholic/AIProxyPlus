using AiProxy.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AiProxy.Infrastructure.Persistence.Configurations;

public sealed class ConsumerConfiguration : IEntityTypeConfiguration<Consumer>
{
    public void Configure(EntityTypeBuilder<Consumer> builder)
    {
        builder.ToTable("consumers");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();

        builder.Property(c => c.Name).IsRequired().HasMaxLength(200);
        builder.HasIndex(c => c.Name).IsUnique();

        builder.Property(c => c.ApiKeyHash).IsRequired().HasMaxLength(500);
        builder.HasIndex(c => c.ApiKeyHash).IsUnique();

        builder.Property(c => c.CreatedAt);
        builder.Property(c => c.RevokedAt);
    }
}
