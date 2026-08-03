using AiProxy.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AiProxy.Infrastructure.Persistence.Configurations;

public sealed class ModelDeploymentEndpointConfiguration : IEntityTypeConfiguration<ModelDeploymentEndpoint>
{
    public void Configure(EntityTypeBuilder<ModelDeploymentEndpoint> builder)
    {
        builder.ToTable("model_deployment_endpoints");
        builder.HasKey(e => new { e.ModelDeploymentId, e.EndpointId });

        builder.Property(e => e.Health).HasConversion<string>().HasMaxLength(50);
        builder.Ignore(e => e.Id);
        builder.Ignore(e => e.Name);
        builder.Ignore(e => e.BaseUrl);
        builder.Ignore(e => e.ApiFamily);
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
    }
}