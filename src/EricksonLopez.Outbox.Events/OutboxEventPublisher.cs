// Copyright © Erickson Lopez. MIT License.
using System;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.Events.Contracts;
using EricksonLopez.Outbox.Persistence;

namespace EricksonLopez.Outbox.Events;

/// <summary>
/// Provides event publishing by atomically storing events into a transactional outbox.
/// </summary>
public sealed class OutboxEventPublisher : IEventPublisher
{
    private readonly IOutbox _outbox;
    private readonly IOutboxTransactionProvider _transactionProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="OutboxEventPublisher"/> class with the specified outbox service and transaction provider.
    /// </summary>
    /// <param name="outbox">The outbox persistence service.</param>
    /// <param name="transactionProvider">The optional transaction provider. If <see langword="null"/>, defaults to <see cref="NullOutboxTransactionProvider.Instance"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="outbox"/> is <see langword="null"/></exception>
    public OutboxEventPublisher(
        IOutbox outbox,
        IOutboxTransactionProvider? transactionProvider = null)
    {
        _outbox = outbox ?? throw new ArgumentNullException(nameof(outbox));
        _transactionProvider = transactionProvider ?? NullOutboxTransactionProvider.Instance;
    }

    /// <inheritdoc/>
    /// <exception cref="ArgumentNullException"><paramref name="eventInstance"/> is <see langword="null"/></exception>
    /// <exception cref="InvalidOperationException">No active transaction context was provided by the transaction provider</exception>
    public ValueTask PublishAsync<TEvent>(
        TEvent eventInstance,
        CancellationToken cancellationToken = default) where TEvent : IEvent
    {
        ArgumentNullException.ThrowIfNull(eventInstance);
        cancellationToken.ThrowIfCancellationRequested();

        var txContext = _transactionProvider.CurrentTransaction;
        if (txContext is null)
        {
            throw new InvalidOperationException(
                $"Cannot store event '{typeof(TEvent).Name}' ({eventInstance.Id}) into the outbox because no active transaction context was provided by '{_transactionProvider.GetType().Name}'.");
        }

        var metadata = new OutboxMessageMetadata(
            correlationId: null,
            causationId: null,
            messageType: typeof(TEvent).FullName!);

        return _outbox.StoreAsync(
            eventInstance,
            txContext,
            metadata,
            deliverAt: null,
            cancellationToken);
    }
}
