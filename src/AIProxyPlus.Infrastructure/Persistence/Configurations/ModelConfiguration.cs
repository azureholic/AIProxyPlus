using AIProxyPlus.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AIProxyPlus.Infrastructure.Persistence.Configurations;

public sealed class ModelConfiguration : IEntityTypeConfiguration<Model>
{
    public void Configure(EntityTypeBuilder<Model> builder)
    {
        builder.ToTable("models");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).ValueGeneratedNever();

        builder.Property(d => d.Name).IsRequired().HasMaxLength(200);
        builder.HasIndex(d => d.Name).IsUnique();

        builder.Property(d => d.Strategy).HasConversion<string>().HasMaxLength(50);
        builder.Property(d => d.CreatedAt);
        builder.Property(d => d.UpdatedAt);

        builder.HasMany(d => d.Endpoints)
            .WithOne()
            .HasForeignKey(e => e.ModelId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(d => d.Endpoints).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Ignore(d => d.Capabilities);
        builder.HasMany<ModelCapabilityAssignment>("_capabilities")
            .WithOne()
            .HasForeignKey(c => c.ModelId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation("_capabilities").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
