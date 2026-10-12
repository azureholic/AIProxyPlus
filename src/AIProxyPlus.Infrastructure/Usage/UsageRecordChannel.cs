using System.Threading.Channels;
using AIProxyPlus.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace AIProxyPlus.Infrastructure.Usage;

/// <summary>
/// Bounded, in-memory queue in front of the usage-record writer (architecture.md §10). Ingress code enqueues
/// and returns immediately; a full channel drops the record (logged) rather than blocking the response path.
/// </summary>
public sealed class UsageRecordChannel(ILogger<UsageRecordChannel> logger, int capacity = 1000)
{
    private readonly Channel<UsageRecord> _channel = Channel.CreateBounded<UsageRecord>(
        new BoundedChannelOptions(capacity) { FullMode = BoundedChannelFullMode.Wait, SingleReader = true });

    public ChannelReader<UsageRecord> Reader => _channel.Reader;

    public void Enqueue(UsageRecord usageRecord)
    {
        if (!_channel.Writer.TryWrite(usageRecord))
        {
            logger.LogError(
                "Usage record queue is full; dropping usage record for request {RequestId} (model {ModelName}).",
                usageRecord.RequestId,
                usageRecord.ModelName);
        }
    }
}
