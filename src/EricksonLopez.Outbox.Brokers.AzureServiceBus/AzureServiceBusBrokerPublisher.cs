// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Azure.Messaging.ServiceBus;
using EricksonLopez.Outbox;
using EricksonLopez.Outbox.Serialization;

namespace EricksonLopez.Outbox.Brokers.AzureServiceBus;

/// <summary>
/// Provides a broker publisher implementation that dispatches outbox messages to Azure Service Bus.
/// </summary>
public sealed class AzureServiceBusBrokerPublisher : IBrokerPublisher
{
    private readonly ServiceBusSender _sender;
    private readonly IOutboxSerializer _serializer;

    /// <summary>
    /// Initializes a new instance of the <see cref="AzureServiceBusBrokerPublisher"/> class.
    /// </summary>
    /// <param name="sender">The Azure Service Bus sender client.</param>
    /// <param name="serializer">The serializer that converts message payloads to byte arrays.</param>
    /// <exception cref="ArgumentNullException"><paramref name="sender"/> or <paramref name="serializer"/> is <see langword="null"/>.</exception>
    public AzureServiceBusBrokerPublisher(ServiceBusSender sender, IOutboxSerializer serializer)
    {
        _sender = sender ?? throw new ArgumentNullException(nameof(sender));
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
    }

    /// <inheritdoc/>
    public async ValueTask<DispatchResult> PublishAsync<T>(MessageEnvelope<T> message, DispatchContext context) where T : notnull
    {
        try
        {
            var serviceBusMessage = CreateServiceBusMessage(message);
            await _sender.SendMessageAsync(serviceBusMessage, context.CancellationToken);
            return DispatchResult.Ok();
        }
        catch (ServiceBusException ex) when (ex.IsTransient)
        {
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
        try
        {
            using var messageBatch = await _sender.CreateMessageBatchAsync(context.CancellationToken);

            foreach (var message in messages)
            {
                var sbMessage = CreateServiceBusMessage(message);
                EnsureMessageAdded(messageBatch, sbMessage);
            }

            await _sender.SendMessagesAsync(messageBatch, context.CancellationToken);
            return messages.Select(x => DispatchResult.Ok()).ToList();
        }
        catch (ServiceBusException ex) when (ex.IsTransient)
        {
            return messages.Select(x => DispatchResult.FailAndRetry(ex)).ToList();
        }
        catch (Exception ex)
        {
            return messages.Select(x => DispatchResult.FailFatal(ex)).ToList();
        }
    }


    private static void EnsureMessageAdded(ServiceBusMessageBatch batch, ServiceBusMessage message)
    {
        if (!batch.TryAddMessage(message))
        {
            // For a robust implementation, if the batch is full, we should send it and create a new one.
            // To keep this adapter simple and matching the interface, we'll process individually or fail.
            throw new InvalidOperationException("Message batch is too large to fit in a single send.");
        }
    }

    private ServiceBusMessage CreateServiceBusMessage<T>(MessageEnvelope<T> message) where T : notnull
    {
        var payloadBytes = _serializer.Serialize(message.Payload).ToArray();
        var sbMessage = new ServiceBusMessage(payloadBytes);

        if (!string.IsNullOrEmpty(message.Metadata.CorrelationId))
        {
            sbMessage.CorrelationId = message.Metadata.CorrelationId;
            sbMessage.SessionId = message.Metadata.CorrelationId; // Crucial for ASB FIFO (Sessions)
        }
        
        if (!string.IsNullOrEmpty(message.Metadata.MessageType))
        {
            sbMessage.ApplicationProperties["MessageType"] = message.Metadata.MessageType;
            sbMessage.Subject = message.Metadata.MessageType;
        }

        if (!string.IsNullOrEmpty(message.Metadata.CausationId))
        {
            sbMessage.ApplicationProperties["CausationId"] = message.Metadata.CausationId;
        }

        foreach (var header in message.Metadata.Entries.Span)
        {
            sbMessage.ApplicationProperties[header.Key] = header.Value;
        }

        return sbMessage;
    }

    /// <inheritdoc/>
    public async ValueTask<DispatchResult> PublishRawAsync(
        OutboxMessage message,
        OutboxMessageMetadata metadata,
        DispatchContext context)
    {
        try
        {
            var sbMessage = new Azure.Messaging.ServiceBus.ServiceBusMessage(message.Payload.ToArray())
            {
                Subject = message.MessageType,
                CorrelationId = metadata.CorrelationId,
                ApplicationProperties = { ["MessageType"] = message.MessageType }
            };

            if (metadata.CausationId is not null)
                sbMessage.ApplicationProperties["CausationId"] = metadata.CausationId;

            foreach (var header in metadata.Entries.Span)
                sbMessage.ApplicationProperties[header.Key] = header.Value;

            await _sender.SendMessageAsync(sbMessage, context.CancellationToken);
            return DispatchResult.Ok();
        }
        catch (Exception ex)
        {
            return DispatchResult.FailAndRetry(ex);
        }
    }
}





