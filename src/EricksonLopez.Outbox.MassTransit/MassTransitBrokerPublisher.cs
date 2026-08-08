using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MassTransit;
using EricksonLopez.Outbox;

namespace EricksonLopez.Outbox.MassTransit;

/// <summary>
/// Publishes outbox messages to a message broker via MassTransit.
/// </summary>
/// <remarks>
/// This bridge allows using EricksonLopez.Outbox for extreme database performance while delegating
/// complex routing and topologies to MassTransit.
/// </remarks>
public sealed class MassTransitBrokerPublisher : IBrokerPublisher
{
    private readonly IPublishEndpoint _publishEndpoint;

    /// <summary>
    /// Initializes a new instance of the <see cref="MassTransitBrokerPublisher"/> class.
    /// </summary>
    /// <param name="publishEndpoint">The MassTransit endpoint that publishes messages.</param>
    /// <exception cref="ArgumentNullException"><paramref name="publishEndpoint"/> is <see langword="null"/>.</exception>
    public MassTransitBrokerPublisher(IPublishEndpoint publishEndpoint)
    {
        _publishEndpoint = publishEndpoint ?? throw new ArgumentNullException(nameof(publishEndpoint));
    }

    private sealed class PublishContextConfigurator<TMessage> where TMessage : notnull
    {
        private readonly MessageEnvelope<TMessage> _message;
        public PublishContextConfigurator(MessageEnvelope<TMessage> message) => _message = message;

        public void Configure(PublishContext p)
        {
            if (Guid.TryParse(_message.Metadata.CorrelationId, out var correlationId))
            {
                p.CorrelationId = correlationId;
            }
            
            foreach (var header in _message.Metadata.Entries.Span)
            {
                p.Headers.Set(header.Key, header.Value);
            }
        }
    }

    /// <inheritdoc/>
    /// <remarks>Propagates the correlation ID from <see cref="MessageMetadata"/> and any custom headers to the MassTransit publish context.</remarks>
    public async ValueTask<DispatchResult> PublishAsync<T>(MessageEnvelope<T> message, DispatchContext context) where T : notnull
    {
        try
        {
            var configurator = new PublishContextConfigurator<T>(message);
            await _publishEndpoint.Publish(message.Payload, configurator.Configure, context.CancellationToken);

            return DispatchResult.Ok();
        }
        catch (Exception ex)
        {
            return DispatchResult.FailAndRetry(ex);
        }
    }

    /// <inheritdoc/>
    /// <remarks>Delegates to <see cref="PublishAsync{T}"/> for each message in the batch sequentially.</remarks>
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
    /// <remarks>
    /// MassTransit does not natively support raw byte publishing without a known CLR type. This
    /// implementation publishes a <see cref="System.Collections.Generic.Dictionary{TKey, TValue}"/> envelope with
    /// the UTF-8 decoded payload and message type, relying on the <c>outbox.message_type</c> header
    /// for consumer routing.
    /// </remarks>
    public async ValueTask<DispatchResult> PublishRawAsync(
        OutboxMessage message,
        MessageMetadata metadata,
        DispatchContext context)
    {
        try
        {
            // MassTransit does not natively support raw byte publish without a known CLR type.
            // We publish as a Dictionary<string, object> envelope, which MassTransit can route
            // using the MessageType header. This is the recommended bridge approach.
            var envelope = new Dictionary<string, object?>
            {
                ["payload"] = System.Text.Encoding.UTF8.GetString(message.Payload.Span),
                ["messageType"] = message.MessageType
            };

            await _publishEndpoint.Publish(envelope, p =>
            {
                if (Guid.TryParse(metadata.CorrelationId, out var corr))
                    p.CorrelationId = corr;

                p.Headers.Set("outbox.message_type", message.MessageType);

                foreach (var header in metadata.Entries.Span)
                    p.Headers.Set(header.Key, header.Value);
            }, context.CancellationToken);

            return DispatchResult.Ok();
        }
        catch (Exception ex)
        {
            return DispatchResult.FailAndRetry(ex);
        }
    }
}
