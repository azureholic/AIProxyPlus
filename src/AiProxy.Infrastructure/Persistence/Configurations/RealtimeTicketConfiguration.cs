using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AiProxy.Infrastructure.Persistence.Configurations;

public sealed class RealtimeTicketConfiguration : IEntityTypeConfiguration<RealtimeTicket>
{
    public void Configure(EntityTypeBuilder<RealtimeTicket> builder)
    {
        builder.ToTable("realtime_tickets");
        builder.HasKey(ticket => ticket.TicketHash);
        builder.Property(ticket => ticket.TicketHash).HasMaxLength(64);
        builder.Property(ticket => ticket.Route).IsRequired().HasMaxLength(200);
        builder.HasIndex(ticket => ticket.ExpiresAt);
        builder.HasOne(ticket => ticket.Consumer)
            .WithMany()
            .HasForeignKey(ticket => ticket.ConsumerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}