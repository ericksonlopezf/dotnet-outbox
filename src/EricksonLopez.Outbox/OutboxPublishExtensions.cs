// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.Outbox.Persistence;

namespace EricksonLopez.Outbox;

/// <summary>
/// Provides extension methods for <see cref="IOutbox"/> to facilitate fluent message publishing and mocking.
/// </summary>
public static class OutboxPublishExtensions
{
    /// <summary>
    /// Enqueues an event or message directly into the outbox within the specified database transaction.
    /// </summary>
    /// <typeparam name="TMessage">The type of the message being stored.</typeparam>
    /// <param name="outbox">The outbox instance.</param>
    /// <param name="message">The message instance to store.</param>
    /// <param name="transaction">The active database transaction context.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous store operation.</returns>
    public static ValueTask EnqueueAsync<TMessage>(
        this IOutbox outbox,
        TMessage message,
        IOutboxTransactionContext transaction,
        CancellationToken cancellationToken = default)
        where TMessage : class
    {
        ArgumentNullException.ThrowIfNull(outbox);
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(transaction);

        return outbox.StoreAsync(message, transaction, cancellationToken);
    }

    /// <summary>
    /// Enqueues a batch of messages directly into the outbox within the specified database transaction.
    /// </summary>
    /// <typeparam name="TMessage">The type of the messages being stored.</typeparam>
    /// <param name="outbox">The outbox instance.</param>
    /// <param name="messages">A memory region containing the messages to store.</param>
    /// <param name="transaction">The active database transaction context.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous batch store operation.</returns>
    public static ValueTask EnqueueAsync<TMessage>(
        this IOutbox outbox,
        ReadOnlyMemory<TMessage> messages,
        IOutboxTransactionContext transaction,
        CancellationToken cancellationToken = default)
        where TMessage : class
    {
        ArgumentNullException.ThrowIfNull(outbox);
        ArgumentNullException.ThrowIfNull(transaction);

        return outbox.StoreAsync(messages, transaction, cancellationToken);
    }

    /// <summary>
    /// Enqueues an enumerable collection of messages directly into the outbox within the specified database transaction.
    /// </summary>
    /// <typeparam name="TMessage">The type of the messages being stored.</typeparam>
    /// <param name="outbox">The outbox instance.</param>
    /// <param name="messages">The collection of messages to store.</param>
    /// <param name="transaction">The active database transaction context.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous batch store operation.</returns>
    public static ValueTask EnqueueAsync<TMessage>(
        this IOutbox outbox,
        IEnumerable<TMessage> messages,
        IOutboxTransactionContext transaction,
        CancellationToken cancellationToken = default)
        where TMessage : class
    {
        ArgumentNullException.ThrowIfNull(outbox);
        ArgumentNullException.ThrowIfNull(messages);
        ArgumentNullException.ThrowIfNull(transaction);

        return outbox.StoreAsync(messages, transaction, cancellationToken);
    }

    /// <summary>
    /// Enqueues an event or message with explicit metadata and scheduling directly into the outbox.
    /// </summary>
    /// <typeparam name="TMessage">The type of the message being stored.</typeparam>
    /// <param name="outbox">The outbox instance.</param>
    /// <param name="message">The message instance to store.</param>
    /// <param name="transaction">The active database transaction context.</param>
    /// <param name="metadata">The metadata associated with the message.</param>
    /// <param name="deliverAt">An optional future timestamp indicating when the message should be dispatched.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous store operation.</returns>
    public static ValueTask EnqueueAsync<TMessage>(
        this IOutbox outbox,
        TMessage message,
        IOutboxTransactionContext transaction,
        OutboxMessageMetadata metadata,
        DateTimeOffset? deliverAt = null,
        CancellationToken cancellationToken = default)
        where TMessage : class
    {
        ArgumentNullException.ThrowIfNull(outbox);
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(transaction);

        return outbox.StoreAsync(message, transaction, metadata, deliverAt, cancellationToken);
    }

    /// <summary>
    /// Begins a fluent message-building chain for enriching a message before persisting it.
    /// </summary>
    /// <typeparam name="TMessage">The type of the message being built.</typeparam>
    /// <param name="outbox">The outbox instance.</param>
    /// <param name="message">The initial message payload to begin enriching.</param>
    /// <returns>A fluent builder instance to configure transaction, delay, and metadata.</returns>
    public static OutboxMessageBuilder<TMessage> Publish<TMessage>(this IOutbox outbox, TMessage message) where TMessage : notnull
    {
        ArgumentNullException.ThrowIfNull(outbox);
        return new OutboxMessageBuilder<TMessage>(outbox, message);
    }
}




