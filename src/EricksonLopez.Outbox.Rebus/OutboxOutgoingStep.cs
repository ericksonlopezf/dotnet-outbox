// Copyright © Erickson Lopez. MIT License.
using System;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.Outbox;
using EricksonLopez.Outbox.Persistence;
using Rebus.Messages;
using Rebus.Pipeline;

namespace EricksonLopez.Outbox.Rebus;

/// <summary>
/// Provides a Rebus outgoing pipeline step that captures messages and routes them through the transactional outbox (<see cref="IOutbox"/>).
/// </summary>
public sealed class OutboxOutgoingStep : IOutgoingStep
{
    private readonly IOutbox _outbox;

    /// <summary>
    /// Initializes a new instance of the <see cref="OutboxOutgoingStep"/> class.
    /// </summary>
    /// <param name="outbox">The outbox instance.</param>
    /// <exception cref="ArgumentNullException"><paramref name="outbox"/> is <see langword="null"/>.</exception>
    public OutboxOutgoingStep(IOutbox outbox)
    {
        _outbox = outbox ?? throw new ArgumentNullException(nameof(outbox));
    }

    /// <inheritdoc/>
    public async Task Process(OutgoingStepContext context, Func<Task> next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var txContext = context.Load<IOutboxTransactionContext>();
        if (txContext != null)
        {
            var message = context.Load<Message>();
            if (message?.Body != null)
            {
                await _outbox.StoreAsync(message.Body, txContext).ConfigureAwait(false);
            }
        }

        await next().ConfigureAwait(false);
    }
}



