using System.Text.Json;
using AiProxy.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AiProxy.Infrastructure.Persistence.Configurations;

public sealed class UsageRecordConfiguration : IEntityTypeConfiguration<UsageRecord>
{
    public void Configure(EntityTypeBuilder<UsageRecord> builder)
    {
        builder.ToTable("usage_records");
        builder.HasKey(u => u.Id);
        builder.Property(u => u.Id).ValueGeneratedNever();

        builder.Property(u => u.ModelDeploymentName).IsRequired().HasMaxLength(200);
        builder.Property(u => u.ConsumerName).IsRequired().HasMaxLength(200);
        builder.Property(u => u.ApiFamily).HasConversion<string>().HasMaxLength(50);
        builder.Property(u => u.OperationType).HasConversion<string>().HasMaxLength(50);
        builder.Property(u => u.RequestId).IsRequired().HasMaxLength(200);
        builder.Property(u => u.SessionId).HasMaxLength(200);

        var usageMetricsComparer = new ValueComparer<IReadOnlyDictionary<string, string>?>(
            (left, right) => DictionariesEqual(left, right),
            dictionary => dictionary == null ? 0 : dictionary.Aggregate(0, (hash, kvp) => HashCode.Combine(hash, kvp.Key, kvp.Value)),
            dictionary => dictionary == null ? null : new Dictionary<string, string>(dictionary));

        builder.Property(u => u.UsageMetrics)
            .HasConversion(
                value => value == null ? null : JsonSerializer.Serialize(value, (JsonSerializerOptions?)null),
                json => string.IsNullOrEmpty(json) ? null : JsonSerializer.Deserialize<Dictionary<string, string>>(json, (JsonSerializerOptions?)null))
            .Metadata.SetValueComparer(usageMetricsComparer);
        builder.Property(u => u.UsageMetrics).HasColumnType("jsonb");

        // Chargeback queries per architecture.md §7.1.
        builder.HasIndex(u => new { u.ModelDeploymentName, u.Timestamp });
        builder.HasIndex(u => new { u.ConsumerId, u.Timestamp });
    }

    private static bool DictionariesEqual(IReadOnlyDictionary<string, string>? left, IReadOnlyDictionary<string, string>? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null || left.Count != right.Count)
        {
            return false;
        }

        return left.All(kvp => right.TryGetValue(kvp.Key, out var value) && value == kvp.Value);
    }
}
