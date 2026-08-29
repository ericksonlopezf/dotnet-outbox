// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.Outbox;
using NATS.Client.Core;

namespace EricksonLopez.Outbox.Brokers.Nats;

/// <summary>
/// Provides a broker publisher implementation that dispatches outbox messages to NATS using the official NATS.Net v2 client.
/// </summary>
/// <remarks>
/// Design decisions:
///   - Uses NATS subject-per-message-type routing (e.g. "order.created.v1").
///   - Leverages NATS headers to propagate correlation and causation IDs.
///   - PublishRawAsync sends the raw payload bytes directly — avoids a double-serialization round-trip.
/// </remarks>
public sealed class NatsBrokerPublisher : IBrokerPublisher
{
    private readonly INatsConnection _connection;

    /// <summary>
    /// Initializes a new instance of the <see cref="NatsBrokerPublisher"/> class.
    /// </summary>
    /// <param name="connection">The NATS connection client.</param>
    /// <exception cref="ArgumentNullException"><paramref name="connection"/> is <see langword="null"/>.</exception>
    public NatsBrokerPublisher(INatsConnection connection)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
    }

    /// <inheritdoc/>
    public async ValueTask<DispatchResult> PublishAsync<T>(
        MessageEnvelope<T> message,
        DispatchContext context) where T : notnull
    {
        try
        {
            var subject = message.Metadata.MessageType ?? typeof(T).Name;
            var headers = BuildHeaders(message.Metadata);

            await _connection.PublishAsync(
                subject,
                message,
                headers: headers,
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
            var headers = BuildHeaders(metadata);

            await _connection.PublishAsync(
                subject: message.MessageType,
                data: message.Payload.ToArray(),
                headers: headers,
                cancellationToken: context.CancellationToken);

            return DispatchResult.Ok();
        }
        catch (Exception ex)
        {
            return DispatchResult.FailAndRetry(ex);
        }
    }

    private static NatsHeaders BuildHeaders(OutboxMessageMetadata metadata)
    {
        var headers = new NatsHeaders();

        if (metadata.CorrelationId is not null)
            headers["X-Correlation-Id"] = metadata.CorrelationId;

        if (metadata.CausationId is not null)
            headers["X-Causation-Id"] = metadata.CausationId;

        foreach (var entry in metadata.Entries.Span)
        {
            headers[entry.Key] = entry.Value;
        }

        return headers;
    }
}





