using AIProxyPlus.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AIProxyPlus.Infrastructure.Persistence.Configurations;

public sealed class ModelCapabilityAssignmentConfiguration : IEntityTypeConfiguration<ModelCapabilityAssignment>
{
    public void Configure(EntityTypeBuilder<ModelCapabilityAssignment> builder)
    {
        builder.ToTable("model_capabilities");
        builder.HasKey(capability => new { capability.ModelId, capability.Capability });
        builder.Property(capability => capability.Capability).HasConversion<string>().HasMaxLength(50);
    }
}