using AiProxy.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AiProxy.Infrastructure.Persistence.Configurations;

public sealed class SessionAffinityEntryConfiguration : IEntityTypeConfiguration<SessionAffinityEntry>
{
    public void Configure(EntityTypeBuilder<SessionAffinityEntry> builder)
    {
        builder.ToTable("session_affinity");
        builder.HasKey(e => e.SessionId);

        builder.Property(e => e.SessionId).HasMaxLength(200);
        builder.Property(e => e.LastUsedAt);
        builder.Property(e => e.ExpiresAt);
        builder.HasIndex(e => e.ExpiresAt);
    }
}
