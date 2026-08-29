// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.Outbox;
using EricksonLopez.Outbox.Serialization;
using RabbitMQ.Client;

namespace EricksonLopez.Outbox.RabbitMQ;

/// <summary>
/// Provides a broker publisher implementation that dispatches outbox messages to a RabbitMQ exchange using the asynchronous client.
/// </summary>
public sealed class RabbitMQBrokerPublisher : IBrokerPublisher
{
    private readonly IChannel _channel;
    private readonly IOutboxSerializer _serializer;
    private readonly string _exchangeName;

    /// <summary>
    /// Initializes a new instance of the <see cref="RabbitMQBrokerPublisher"/> class.
    /// </summary>
    /// <param name="channel">The RabbitMQ communication channel.</param>
    /// <param name="serializer">The serializer that encodes the message payload.</param>
    /// <param name="exchangeName">The target exchange name for publishing messages.</param>
    /// <exception cref="ArgumentNullException"><paramref name="channel"/> or <paramref name="serializer"/> is <see langword="null"/>.</exception>
    public RabbitMQBrokerPublisher(IChannel channel, IOutboxSerializer serializer, string exchangeName = "outbox.exchange")
    {
        _channel = channel ?? throw new ArgumentNullException(nameof(channel));
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        _exchangeName = exchangeName;
    }

    /// <inheritdoc/>
    public async ValueTask<DispatchResult> PublishAsync<T>(
        MessageEnvelope<T> message, 
        DispatchContext context) where T : notnull
    {
        try
        {
            var properties = new BasicProperties
            {
                CorrelationId = message.Metadata.CorrelationId,
                Headers = new Dictionary<string, object?>()
            };
            
            foreach (var header in message.Metadata.Entries.Span)
            {
                properties.Headers[header.Key] = header.Value;
            }

            var payloadBytes = _serializer.Serialize(message.Payload);

            // Client v7 is fully async and accepts ReadOnlyMemory directly, avoiding allocations
            await _channel.BasicPublishAsync(
                exchange: _exchangeName, 
                routingKey: message.Metadata.MessageType ?? "", 
                mandatory: true, 
                basicProperties: properties, 
                body: payloadBytes, 
                cancellationToken: context.CancellationToken);

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
        OutboxMessageMetadata metadata,
        DispatchContext context)
    {
        try
        {
            var properties = new BasicProperties
            {
                CorrelationId = metadata.CorrelationId,
                Headers = new Dictionary<string, object?>()
            };

            foreach (var header in metadata.Entries.Span)
            {
                properties.Headers[header.Key] = header.Value;
            }

            await _channel.BasicPublishAsync(
                exchange: _exchangeName,
                routingKey: message.MessageType,
                mandatory: true,
                basicProperties: properties,
                body: message.Payload,
                cancellationToken: context.CancellationToken);

            return DispatchResult.Ok();
        }
        catch (Exception ex)
        {
            return DispatchResult.FailAndRetry(ex);
        }
    }
}





