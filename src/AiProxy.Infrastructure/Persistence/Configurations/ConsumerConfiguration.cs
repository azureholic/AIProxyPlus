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

        builder.Property(c => c.ApiKeyHash).HasMaxLength(500);
        builder.HasIndex(c => c.ApiKeyHash)
            .IsUnique()
            .HasFilter("\"ApiKeyHash\" IS NOT NULL");

        builder.Property(c => c.OidcIssuer).HasMaxLength(500);
        builder.Property(c => c.OidcSubject).HasMaxLength(500);
        builder.HasIndex(c => new { c.OidcIssuer, c.OidcSubject })
            .IsUnique()
            .HasFilter("\"OidcIssuer\" IS NOT NULL AND \"OidcSubject\" IS NOT NULL");

        builder.ToTable(
            "consumers",
            table => table.HasCheckConstraint(
                "CK_consumers_authentication_identity",
                "(\"ApiKeyHash\" IS NOT NULL AND \"OidcIssuer\" IS NULL AND \"OidcSubject\" IS NULL) OR " +
                "(\"ApiKeyHash\" IS NULL AND \"OidcIssuer\" IS NOT NULL AND \"OidcSubject\" IS NOT NULL)"));

        builder.Property(c => c.CreatedAt);
        builder.Property(c => c.RevokedAt);
    }
}
