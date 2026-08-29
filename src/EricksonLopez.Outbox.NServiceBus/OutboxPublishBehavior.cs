// Copyright © Erickson Lopez. MIT License.
using System;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.Outbox;
using EricksonLopez.Outbox.Persistence;
using NServiceBus.Pipeline;

namespace EricksonLopez.Outbox.NServiceBus;

/// <summary>
/// Provides an NServiceBus pipeline behavior that captures outgoing published and sent messages and routes them
/// through the transactional outbox (<see cref="IOutbox"/>).
/// </summary>
public sealed class OutboxPublishBehavior : Behavior<IOutgoingLogicalMessageContext>
{
    private readonly IOutbox _outbox;

    /// <summary>
    /// Initializes a new instance of the <see cref="OutboxPublishBehavior"/> class.
    /// </summary>
    /// <param name="outbox">The outbox instance used to persist messages.</param>
    /// <exception cref="ArgumentNullException"><paramref name="outbox"/> is <see langword="null"/>.</exception>
    public OutboxPublishBehavior(IOutbox outbox)
    {
        _outbox = outbox ?? throw new ArgumentNullException(nameof(outbox));
    }

    /// <inheritdoc/>
    public override async Task Invoke(IOutgoingLogicalMessageContext context, Func<Task> next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        if (context.Extensions.TryGet<IOutboxTransactionContext>(out var transactionContext) && transactionContext != null)
        {
            var messageInstance = context.Message.Instance;
            if (messageInstance != null)
            {
                await _outbox.StoreAsync(messageInstance, transactionContext, context.CancellationToken).ConfigureAwait(false);
            }
        }

        await next().ConfigureAwait(false);
    }
}




