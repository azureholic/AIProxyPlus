using AiProxy.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AiProxy.Infrastructure.Persistence.Configurations;

public sealed class ModelDeploymentConfiguration : IEntityTypeConfiguration<ModelDeployment>
{
    public void Configure(EntityTypeBuilder<ModelDeployment> builder)
    {
        builder.ToTable("model_deployments");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).ValueGeneratedNever();

        builder.Property(d => d.Name).IsRequired().HasMaxLength(200);
        builder.HasIndex(d => d.Name).IsUnique();

        builder.Property(d => d.Strategy).HasConversion<string>().HasMaxLength(50);
        builder.Property(d => d.StickySessionEnabled);
        builder.Property(d => d.CreatedAt);
        builder.Property(d => d.UpdatedAt);

        builder.HasMany(d => d.Endpoints)
            .WithOne()
            .HasForeignKey(e => e.ModelDeploymentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(d => d.Endpoints).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
