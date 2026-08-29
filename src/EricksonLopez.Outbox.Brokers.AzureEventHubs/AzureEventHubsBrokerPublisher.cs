// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Azure.Messaging.EventHubs;
using Azure.Messaging.EventHubs.Producer;
using EricksonLopez.Outbox;
using EricksonLopez.Outbox.Serialization;

namespace EricksonLopez.Outbox.Brokers.AzureEventHubs;

/// <summary>
/// Provides a broker publisher implementation that dispatches outbox messages to Azure Event Hubs.
/// </summary>
public sealed class AzureEventHubsBrokerPublisher : IBrokerPublisher, ITypedBrokerPublisher
{
    private readonly EventHubProducerClient _producerClient;
    private readonly IOutboxSerializer? _serializer;

    /// <inheritdoc/>
    public string BrokerSystemName => "azure_event_hubs";

    /// <summary>
    /// Initializes a new instance of the <see cref="AzureEventHubsBrokerPublisher"/> class.
    /// </summary>
    /// <param name="producerClient">The Azure Event Hubs producer client.</param>
    /// <param name="serializer">The optional serializer for strongly typed publishing.</param>
    /// <exception cref="ArgumentNullException"><paramref name="producerClient"/> is <see langword="null"/>.</exception>
    public AzureEventHubsBrokerPublisher(EventHubProducerClient producerClient, IOutboxSerializer? serializer = null)
    {
        ArgumentNullException.ThrowIfNull(producerClient);
        _producerClient = producerClient;
        _serializer = serializer;
    }

    /// <inheritdoc/>
    public async ValueTask<DispatchResult> PublishAsync<T>(MessageEnvelope<T> message, DispatchContext context) where T : notnull
    {
        if (_serializer is null)
        {
            return DispatchResult.FailFatal(new InvalidOperationException("No IOutboxSerializer was provided for typed publishing."));
        }

        try
        {
            var payloadBytes = _serializer.Serialize(message.Payload);
            var eventData = new EventData(payloadBytes);

            if (!string.IsNullOrEmpty(message.Metadata.CorrelationId))
            {
                eventData.Properties["CorrelationId"] = message.Metadata.CorrelationId;
            }

            if (!string.IsNullOrEmpty(message.Metadata.MessageType))
            {
                eventData.Properties["MessageType"] = message.Metadata.MessageType;
            }

            if (!string.IsNullOrEmpty(message.Metadata.CausationId))
            {
                eventData.Properties["CausationId"] = message.Metadata.CausationId;
            }

            foreach (var header in message.Metadata.Entries.Span)
            {
                eventData.Properties[header.Key] = header.Value;
            }

            var options = new SendEventOptions();
            if (!string.IsNullOrEmpty(message.Metadata.CorrelationId))
            {
                options.PartitionKey = message.Metadata.CorrelationId;
            }

            await _producerClient.SendAsync(new[] { eventData }, options, context.CancellationToken).ConfigureAwait(false);
            return DispatchResult.Ok();
        }
        catch (EventHubsException ex) when (ex.IsTransient)
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
        var results = new List<DispatchResult>(messages.Count);
        foreach (var message in messages)
        {
            results.Add(await PublishAsync(message, context).ConfigureAwait(false));
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
            var eventData = new EventData(message.Payload);

            eventData.Properties["MessageType"] = message.MessageType;

            if (!string.IsNullOrEmpty(metadata.CorrelationId))
            {
                eventData.Properties["CorrelationId"] = metadata.CorrelationId;
            }

            if (!string.IsNullOrEmpty(metadata.CausationId))
            {
                eventData.Properties["CausationId"] = metadata.CausationId;
            }

            if (!string.IsNullOrEmpty(message.TenantId))
            {
                eventData.Properties["TenantId"] = message.TenantId;
            }

            foreach (var header in metadata.Entries.Span)
            {
                eventData.Properties[header.Key] = header.Value;
            }

            var options = new SendEventOptions();
            if (!string.IsNullOrEmpty(metadata.CorrelationId))
            {
                options.PartitionKey = metadata.CorrelationId;
            }
            else if (!string.IsNullOrEmpty(message.TenantId))
            {
                options.PartitionKey = message.TenantId;
            }

            await _producerClient.SendAsync(new[] { eventData }, options, context.CancellationToken).ConfigureAwait(false);
            return DispatchResult.Ok();
        }
        catch (EventHubsException ex) when (ex.IsTransient)
        {
            return DispatchResult.FailAndRetry(ex);
        }
        catch (Exception ex)
        {
            return DispatchResult.FailFatal(ex);
        }
    }
}

