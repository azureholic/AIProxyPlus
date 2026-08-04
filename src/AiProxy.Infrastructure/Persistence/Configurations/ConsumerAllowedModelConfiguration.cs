using AiProxy.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AiProxy.Infrastructure.Persistence.Configurations;

public sealed class ConsumerAllowedModelConfiguration : IEntityTypeConfiguration<ConsumerAllowedModel>
{
    public void Configure(EntityTypeBuilder<ConsumerAllowedModel> builder)
    {
        builder.ToTable("consumer_allowed_models");
        builder.HasKey(a => new { a.ConsumerId, a.ModelId });

        // The Consumer-side FK (cascade) is configured from ConsumerConfiguration's "_allowedModels" navigation;
        // this configures the other side — deleting a Model also removes any consumer's allow-list entry for it.
        builder.HasOne<Model>()
            .WithMany()
            .HasForeignKey(a => a.ModelId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
