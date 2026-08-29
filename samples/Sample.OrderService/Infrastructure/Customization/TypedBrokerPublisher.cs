// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.Outbox;
using Microsoft.Extensions.Logging;

namespace Sample.OrderService.Infrastructure.Customization;

/// <summary>
/// Showcase Typed Publisher
/// Demonstrates how an <see cref="ITypedBrokerPublisher"/> handles fully deserialized messages.
/// Instead of raw bytes, the broker receives the strongly typed <see cref="MessageEnvelope{T}"/>.
/// </summary>
public sealed class TypedBrokerPublisher : ITypedBrokerPublisher
{
    private readonly ILogger<TypedBrokerPublisher> _logger;

    public TypedBrokerPublisher(ILogger<TypedBrokerPublisher> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public ValueTask<DispatchResult> PublishAsync<T>(
        MessageEnvelope<T> message,
        DispatchContext context) where T : notnull
    {
        _logger.LogInformation(
            "Typed Publisher: Sending message of CLR type {Type} with CorrelationId {CorrelationId}. Attempt {Attempt}",
            typeof(T).Name,
            message.Metadata.CorrelationId,
            context.Attempt);

        // We can access properties of the payload directly
        if (message.Payload is Domain.Aggregates.OrderAggregate.OrderCreatedEvent createdEvent)
        {
            _logger.LogInformation("Order total is: {Total}", createdEvent.Total);
        }

        return ValueTask.FromResult(DispatchResult.Ok());
    }

    /// <inheritdoc/>
    public ValueTask<IReadOnlyList<DispatchResult>> PublishBatchAsync<T>(
        IReadOnlyList<MessageEnvelope<T>> messages,
        DispatchContext context) where T : notnull
    {
        _logger.LogInformation("Typed Publisher: Batch dispatch of {Count} messages.", messages.Count);
        
        var results = new DispatchResult[messages.Count];
        for (int i = 0; i < messages.Count; i++)
        {
            results[i] = DispatchResult.Ok();
        }

        return ValueTask.FromResult<IReadOnlyList<DispatchResult>>(results);
    }

    /// <inheritdoc/>
    public ValueTask<DispatchResult> PublishRawAsync(
        OutboxMessage message,
        OutboxMessageMetadata metadata,
        DispatchContext context)
    {
        // ITypedBrokerPublisher inherits from IBrokerPublisher.
        // The dispatcher calls this if it fails to resolve the CLR type (fallback),
        // or if you haven't configured a serializer.
        _logger.LogWarning("Fallback to raw dispatch for message {MessageId}", message.Id);
        return ValueTask.FromResult(DispatchResult.Ok());
    }
}





