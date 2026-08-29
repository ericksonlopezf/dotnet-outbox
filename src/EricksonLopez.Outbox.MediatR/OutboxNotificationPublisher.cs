// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.Outbox;
using EricksonLopez.Outbox.Contracts;
using EricksonLopez.Outbox.Persistence;
using MediatR;

namespace EricksonLopez.Outbox.MediatR;

/// <summary>
/// Provides a MediatR <see cref="INotificationPublisher"/> implementation that seamlessly routes
/// notifications to the transactional outbox (<see cref="IOutbox"/>) when active, while dispatching
/// to in-process handlers.
/// </summary>
public sealed class OutboxNotificationPublisher : INotificationPublisher
{
    private readonly IOutbox _outbox;
    private readonly IOutboxTransactionContext? _transactionContext;

    /// <summary>
    /// Initializes a new instance of the <see cref="OutboxNotificationPublisher"/> class.
    /// </summary>
    /// <param name="outbox">The outbox instance used to persist integration messages.</param>
    /// <param name="transactionContext">The optional active transaction context.</param>
    /// <exception cref="ArgumentNullException"><paramref name="outbox"/> is <see langword="null"/>.</exception>
    public OutboxNotificationPublisher(
        IOutbox outbox,
        IOutboxTransactionContext? transactionContext = null)
    {
        _outbox = outbox ?? throw new ArgumentNullException(nameof(outbox));
        _transactionContext = transactionContext;
    }

    /// <inheritdoc/>
    public async Task Publish(
        IEnumerable<NotificationHandlerExecutor> handlerExecutors,
        INotification notification,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handlerExecutors);
        ArgumentNullException.ThrowIfNull(notification);

        // 1. If the notification has [OutboxMessage] and we have an active transaction context, store to Outbox
        var notificationType = notification.GetType();
        var hasOutboxAttr = Attribute.IsDefined(notificationType, typeof(OutboxMessageAttribute));

        if (hasOutboxAttr && _transactionContext is not null)
        {
            await _outbox.StoreAsync(notification, _transactionContext, cancellationToken).ConfigureAwait(false);
        }

        // 2. Dispatch to standard in-process MediatR handlers
        foreach (var handler in handlerExecutors)
        {
            await handler.HandlerCallback(notification, cancellationToken).ConfigureAwait(false);
        }
    }
}



