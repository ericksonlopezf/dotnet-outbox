// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Dapr.Client;
using EricksonLopez.Outbox;

namespace EricksonLopez.Outbox.Dapr;

/// <summary>
/// Provides a broker publisher implementation that dispatches outbox messages using the Dapr Pub/Sub component API.
/// </summary>
public sealed class DaprBrokerPublisher : IBrokerPublisher
{
    private readonly DaprClient _daprClient;
    private readonly string _pubsubName;

    /// <summary>
    /// Initializes a new instance of the <see cref="DaprBrokerPublisher"/> class.
    /// </summary>
    /// <param name="daprClient">The Dapr client instance.</param>
    /// <param name="pubsubName">The name of the configured Dapr Pub/Sub component (default: "pubsub").</param>
    /// <exception cref="ArgumentNullException"><paramref name="daprClient"/> is <see langword="null"/>.</exception>
    public DaprBrokerPublisher(DaprClient daprClient, string pubsubName = "pubsub")
    {
        _daprClient = daprClient ?? throw new ArgumentNullException(nameof(daprClient));
        _pubsubName = string.IsNullOrWhiteSpace(pubsubName) ? "pubsub" : pubsubName;
    }

    /// <inheritdoc/>
    public string BrokerSystemName => "dapr";

    /// <inheritdoc/>
    public async ValueTask<DispatchResult> PublishRawAsync(
        OutboxMessage message,
        OutboxMessageMetadata metadata,
        DispatchContext context)
    {
        ArgumentNullException.ThrowIfNull(message);

        try
        {
            var topic = message.MessageType;
            var daprMetadata = new Dictionary<string, string>(StringComparer.Ordinal);

            if (!string.IsNullOrEmpty(metadata.CorrelationId))
            {
                daprMetadata["correlationId"] = metadata.CorrelationId;
            }

            if (!string.IsNullOrEmpty(metadata.CausationId))
            {
                daprMetadata["causationId"] = metadata.CausationId;
            }

            foreach (var entry in metadata.Entries.Span)
            {
                daprMetadata[entry.Key] = entry.Value;
            }

            using var doc = JsonDocument.Parse(message.Payload);
            await _daprClient.PublishEventAsync(
                _pubsubName,
                topic,
                doc.RootElement,
                daprMetadata,
                context.CancellationToken).ConfigureAwait(false);

            return DispatchResult.Ok();
        }
        catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return DispatchResult.FailAndRetry(ex);
        }
    }
}





