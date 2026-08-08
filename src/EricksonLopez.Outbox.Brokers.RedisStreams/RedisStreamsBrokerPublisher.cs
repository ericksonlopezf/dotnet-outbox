using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using StackExchange.Redis;
using EricksonLopez.Outbox;

namespace EricksonLopez.Outbox.Brokers.RedisStreams;

/// <summary>
/// Publishes outbox messages to Redis Streams using StackExchange.Redis.
/// </summary>
/// <remarks>
/// Design decisions:
///   - Stream key is derived from the message type (e.g. "outbox:order.created.v1").
///   - Uses XADD with MAXLEN ~ to cap stream length and prevent unbounded growth (auto-trim).
///   - Payload stored as "payload" field; metadata headers stored individually.
///   - Redis Streams deliver at-least-once; consumers must implement idempotency via the Inbox pattern.
///
/// Trade-off vs Kafka:
///   + No broker to deploy separately if Redis already exists.
///   + Consumer groups provide pub/sub fan-out with persistence.
///   - Not designed for high-throughput analytics workloads.
/// </remarks>
public sealed class RedisStreamsBrokerPublisher : IBrokerPublisher
{
    private readonly IConnectionMultiplexer _redis;
    private readonly int _maxStreamLength;

    /// <summary>
    /// Initializes a new instance of the <see cref="RedisStreamsBrokerPublisher"/> class.
    /// </summary>
    /// <param name="redis">The Redis connection multiplexer.</param>
    /// <param name="maxStreamLength">The maximum length of the Redis stream. Defaults to 10,000.</param>
    /// <exception cref="ArgumentNullException"><paramref name="redis"/> is <see langword="null"/>.</exception>
    public RedisStreamsBrokerPublisher(IConnectionMultiplexer redis, int maxStreamLength = 10_000)
    {
        _redis = redis ?? throw new ArgumentNullException(nameof(redis));
        _maxStreamLength = maxStreamLength;
    }

    /// <inheritdoc/>
    public async ValueTask<DispatchResult> PublishAsync<T>(
        MessageEnvelope<T> message,
        DispatchContext context) where T : notnull
    {
        try
        {
            var db = _redis.GetDatabase();
            var streamKey = BuildStreamKey(message.Metadata.MessageType ?? typeof(T).Name);
            var fields = BuildFields(message.Metadata, Array.Empty<byte>());

            await db.StreamAddAsync(
                streamKey,
                fields,
                maxLength: _maxStreamLength,
                useApproximateMaxLength: true);

            return DispatchResult.Ok();
        }
        catch (Exception ex)
        {
            return DispatchResult.FailAndRetry(ex);
        }
    }

    /// <inheritdoc/>
    public async ValueTask<IReadOnlyList<DispatchResult>> PublishBatchAsync<T>(
        IReadOnlyList<MessageEnvelope<T>> messages,
        DispatchContext context) where T : notnull
    {
        var results = new List<DispatchResult>(messages.Count);
        foreach (var message in messages)
        {
            results.Add(await PublishAsync(message, context));
        }
        return results;
    }

    /// <inheritdoc/>
    public async ValueTask<DispatchResult> PublishRawAsync(
        OutboxMessage message,
        MessageMetadata metadata,
        DispatchContext context)
    {
        try
        {
            var db = _redis.GetDatabase();
            var streamKey = BuildStreamKey(message.MessageType);
            var fields = BuildFields(metadata, message.Payload.ToArray());

            await db.StreamAddAsync(
                streamKey,
                fields,
                maxLength: _maxStreamLength,
                useApproximateMaxLength: true);

            return DispatchResult.Ok();
        }
        catch (Exception ex)
        {
            return DispatchResult.FailAndRetry(ex);
        }
    }

    private static string BuildStreamKey(string messageType) =>
        $"outbox:{messageType.Replace('.', ':').ToLowerInvariant()}";

    private static NameValueEntry[] BuildFields(MessageMetadata metadata, byte[] payload)
    {
        var fields = new List<NameValueEntry>
        {
            new("payload", payload),
            new("message_type", metadata.MessageType ?? string.Empty)
        };

        if (metadata.CorrelationId is not null)
            fields.Add(new NameValueEntry("correlation_id", metadata.CorrelationId));

        if (metadata.CausationId is not null)
            fields.Add(new NameValueEntry("causation_id", metadata.CausationId));

        foreach (var entry in metadata.Entries.Span)
            fields.Add(new NameValueEntry(entry.Key, entry.Value));

        return fields.ToArray();
    }
}
