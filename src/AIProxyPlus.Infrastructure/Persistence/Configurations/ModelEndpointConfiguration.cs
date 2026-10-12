using AIProxyPlus.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AIProxyPlus.Infrastructure.Persistence.Configurations;

public sealed class ModelEndpointConfiguration : IEntityTypeConfiguration<ModelEndpoint>
{
    public void Configure(EntityTypeBuilder<ModelEndpoint> builder)
    {
        builder.ToTable("model_endpoints");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.HasIndex(e => new { e.ModelId, e.EndpointId, e.ApiRouteId }).IsUnique();

        builder.Property(e => e.Health).HasConversion<string>().HasMaxLength(50);
        builder.Ignore(e => e.Name);
        builder.Ignore(e => e.BaseUrl);
        builder.Ignore(e => e.Provider);
        builder.Ignore(e => e.AuthMode);
        builder.Ignore(e => e.SecretRef);
        builder.Ignore(e => e.ManagedIdentityScope);
        builder.Ignore(e => e.ManagedIdentityTenantId);
        builder.Ignore(e => e.TimeoutSeconds);
        builder.Ignore(e => e.MaxRetries);

        builder.HasOne(e => e.Endpoint)
            .WithMany()
            .HasForeignKey(e => e.EndpointId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.ApiRoute)
            .WithMany()
            .HasForeignKey(e => e.ApiRouteId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}