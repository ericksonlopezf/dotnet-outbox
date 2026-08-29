// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Frozen;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.Outbox;
using EricksonLopez.Outbox.Pipeline;
using Microsoft.Extensions.Logging;

namespace Sample.OrderService.Infrastructure.Customization;

/// <summary>
/// Showcase Middleware 2: Filtering
/// Demonstrates the "Short-Circuit" pattern.
/// Drops specific messages without sending them to the broker.
/// </summary>
public sealed class MessageFilterMiddleware : IOutboxMiddleware
{
    private readonly ILogger<MessageFilterMiddleware> _logger;

    private static readonly FrozenSet<string> BlockedTypes =
        FrozenSet.Create(StringComparer.OrdinalIgnoreCase, Array.Empty<string>());

    public MessageFilterMiddleware(ILogger<MessageFilterMiddleware> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public ValueTask<DispatchResult> InvokeAsync(
        OutboxMessage message,
        OutboxMessageMetadata metadata,
        OutboxPipelineDelegate next,
        CancellationToken cancellationToken)
    {
        if (BlockedTypes.Contains(message.MessageType))
        {
            _logger.LogWarning("Message {MessageId} of type {Type} was blocked by filter.", 
                message.Id, message.MessageType);

            return ValueTask.FromResult(DispatchResult.Ok());
        }

        return next(message, metadata, cancellationToken);
    }
}
