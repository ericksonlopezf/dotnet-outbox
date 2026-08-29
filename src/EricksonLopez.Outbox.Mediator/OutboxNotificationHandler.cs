// Copyright © Erickson Lopez. MIT License.
using System;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.Mediator;
using EricksonLopez.Outbox;
using EricksonLopez.Outbox.Contracts;
using EricksonLopez.Outbox.Persistence;

namespace EricksonLopez.Outbox.Mediator;

/// <summary>
/// Provides a mediator notification handler that routes notifications marked with <see cref="OutboxMessageAttribute"/>
/// to the transactional outbox (<see cref="IOutbox"/>).
/// </summary>
/// <typeparam name="TNotification">The notification type.</typeparam>
public sealed class OutboxNotificationHandler<TNotification> : INotificationHandler<TNotification>
    where TNotification : INotification
{
    private readonly IOutbox _outbox;
    private readonly IOutboxTransactionContext? _transactionContext;

    /// <summary>
    /// Initializes a new instance of the <see cref="OutboxNotificationHandler{TNotification}"/> class.
    /// </summary>
    /// <param name="outbox">The outbox instance used to persist messages.</param>
    /// <param name="transactionContext">The optional active transaction context.</param>
    /// <exception cref="ArgumentNullException"><paramref name="outbox"/> is <see langword="null"/>.</exception>
    public OutboxNotificationHandler(
        IOutbox outbox,
        IOutboxTransactionContext? transactionContext = null)
    {
        _outbox = outbox ?? throw new ArgumentNullException(nameof(outbox));
        _transactionContext = transactionContext;
    }

    /// <inheritdoc/>
    public async ValueTask Handle(TNotification notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);

        var hasOutboxAttr = Attribute.IsDefined(typeof(TNotification), typeof(OutboxMessageAttribute));
        if (hasOutboxAttr && _transactionContext is not null)
        {
            await _outbox.StoreAsync(notification, _transactionContext, cancellationToken).ConfigureAwait(false);
        }
    }
}
