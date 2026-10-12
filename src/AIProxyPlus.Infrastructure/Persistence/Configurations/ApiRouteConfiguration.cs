using AIProxyPlus.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AIProxyPlus.Infrastructure.Persistence.Configurations;

public sealed class ApiRouteConfiguration : IEntityTypeConfiguration<ApiRoute>
{
    public void Configure(EntityTypeBuilder<ApiRoute> builder)
    {
        builder.ToTable("api_routes");
        builder.HasKey(route => route.Id);
        builder.Property(route => route.Id).ValueGeneratedNever();
        builder.Property(route => route.Name).IsRequired().HasMaxLength(200);
        builder.HasIndex(route => route.Name).IsUnique();
        builder.Property(route => route.Provider).HasConversion<string>().HasMaxLength(50);
        builder.Property(route => route.PathPrefix).IsRequired().HasMaxLength(500);
        builder.HasIndex(route => new { route.Provider, route.PathPrefix }).IsUnique();
    }
}