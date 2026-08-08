using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Confluent.Kafka;
using EricksonLopez.Outbox;
using EricksonLopez.Outbox.Serialization;

namespace EricksonLopez.Outbox.Brokers.Kafka;

/// <summary>
/// Publishes outbox messages to an Apache Kafka cluster.
/// </summary>
public sealed class KafkaBrokerPublisher : IBrokerPublisher
{
    private readonly IProducer<byte[], byte[]> _producer;
    private readonly IOutboxSerializer _serializer;
    private readonly string _defaultTopic;

    /// <summary>
    /// Initializes a new instance of the <see cref="KafkaBrokerPublisher"/> class.
    /// </summary>
    /// <param name="producer">The Kafka producer instance that sends messages.</param>
    /// <param name="serializer">The serializer that encodes the message payload.</param>
    /// <param name="defaultTopic">The default Kafka topic to which messages are published if not overridden by metadata.</param>
    /// <exception cref="ArgumentNullException"><paramref name="producer"/> or <paramref name="serializer"/> is <see langword="null"/>.</exception>
    public KafkaBrokerPublisher(IProducer<byte[], byte[]> producer, IOutboxSerializer serializer, string defaultTopic)
    {
        _producer = producer ?? throw new ArgumentNullException(nameof(producer));
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        _defaultTopic = defaultTopic;
    }

    /// <inheritdoc/>
    public async ValueTask<DispatchResult> PublishAsync<T>(MessageEnvelope<T> message, DispatchContext context) where T : notnull
    {
        try
        {
            var headers = new Headers();
            
            if (!string.IsNullOrEmpty(message.Metadata.CorrelationId))
            {
                headers.Add("CorrelationId", Encoding.UTF8.GetBytes(message.Metadata.CorrelationId));
            }
            if (!string.IsNullOrEmpty(message.Metadata.CausationId))
            {
                headers.Add("CausationId", Encoding.UTF8.GetBytes(message.Metadata.CausationId));
            }
            if (!string.IsNullOrEmpty(message.Metadata.MessageType))
            {
                headers.Add("MessageType", Encoding.UTF8.GetBytes(message.Metadata.MessageType));
            }

            foreach (var header in message.Metadata.Entries.Span)
            {
                headers.Add(header.Key, Encoding.UTF8.GetBytes(header.Value));
            }

            // Derivar Partition Key
            byte[]? partitionKey = null;
            var partitionKeyString = message.Metadata.GetValue("Kafka-Partition-Key") ?? message.Metadata.CorrelationId;
            
            if (!string.IsNullOrEmpty(partitionKeyString))
            {
                partitionKey = Encoding.UTF8.GetBytes(partitionKeyString);
            }

            var payloadBytes = _serializer.Serialize(message.Payload).ToArray();

            var kafkaMessage = new Message<byte[], byte[]>
            {
                Key = partitionKey ?? Array.Empty<byte>(),
                Value = payloadBytes,
                Headers = headers
            };

            var topic = message.Metadata.GetValue("Kafka-Topic") ?? _defaultTopic;

            // ProduceAsync expects CancellationToken
            await _producer.ProduceAsync(topic, kafkaMessage, context.CancellationToken);

            return DispatchResult.Ok();
        }
        catch (ProduceException<byte[], byte[]> ex)
        {
            // Transient error in Kafka (like timeout) usually ShouldRetry = true
            return DispatchResult.FailAndRetry(ex);
        }
        catch (Exception ex)
        {
            return DispatchResult.FailFatal(ex);
        }
    }

    /// <inheritdoc/>
    public async ValueTask<IReadOnlyList<DispatchResult>> PublishBatchAsync<T>(IReadOnlyList<MessageEnvelope<T>> messages, DispatchContext context) where T : notnull
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
        EricksonLopez.Outbox.MessageMetadata metadata,
        DispatchContext context)
    {
        try
        {
            var topic = metadata.GetValue("Kafka-Topic") ?? _defaultTopic;

            var kafkaMessage = new Confluent.Kafka.Message<byte[], byte[]>
            {
                Key = System.Text.Encoding.UTF8.GetBytes(message.Id.ToString()),
                Value = message.Payload.ToArray(),
                Headers = new Confluent.Kafka.Headers()
            };

            kafkaMessage.Headers.Add("message_type", System.Text.Encoding.UTF8.GetBytes(message.MessageType));
            if (metadata.CorrelationId is not null)
                kafkaMessage.Headers.Add("correlation_id", System.Text.Encoding.UTF8.GetBytes(metadata.CorrelationId));

            await _producer.ProduceAsync(topic, kafkaMessage, context.CancellationToken);
            return DispatchResult.Ok();
        }
        catch (Confluent.Kafka.ProduceException<byte[], byte[]> ex)
        {
            return DispatchResult.FailAndRetry(ex);
        }
        catch (Exception ex)
        {
            return DispatchResult.FailFatal(ex);
        }
    }
}
