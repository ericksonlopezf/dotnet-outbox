// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.Outbox.Persistence;

namespace EricksonLopez.Outbox;

/// <summary>
/// Defines the primary entry point for storing messages in the outbox using a transaction context.
/// </summary>
public interface IOutbox
{
    /// <summary>
    /// Stores a single message atomically within the specified transaction.
    /// </summary>
    /// <typeparam name="TMessage">The type of the message to store.</typeparam>
    /// <param name="message">The message payload to store in the outbox.</param>
    /// <param name="transaction">The transaction context that scopes this operation.</param>
    /// <param name="cancellationToken">A token that can be used to cancel the asynchronous operation.</param>
    /// <returns>A task representing the asynchronous storage operation.</returns>
    ValueTask StoreAsync<TMessage>(
        TMessage message,
        IOutboxTransactionContext transaction,
        CancellationToken cancellationToken = default) where TMessage : notnull;

    /// <summary>
    /// Stores a batch of messages atomically within the specified transaction.
    /// </summary>
    /// <typeparam name="TMessage">The type of the messages to store.</typeparam>
    /// <param name="messages">A memory region containing the batch of messages to store.</param>
    /// <param name="transaction">The transaction context that scopes this operation.</param>
    /// <param name="cancellationToken">A token that can be used to cancel the asynchronous operation.</param>
    /// <returns>A task representing the asynchronous batch storage operation.</returns>
    ValueTask StoreAsync<TMessage>(
        ReadOnlyMemory<TMessage> messages,
        IOutboxTransactionContext transaction,
        CancellationToken cancellationToken = default) where TMessage : notnull;

    /// <summary>
    /// Stores a single message with explicit metadata and scheduling atomically within the specified transaction.
    /// </summary>
    /// <typeparam name="TMessage">The type of the message to store.</typeparam>
    /// <param name="message">The message payload to store in the outbox.</param>
    /// <param name="transaction">The transaction context that scopes this operation.</param>
    /// <param name="metadata">The metadata associated with the message, such as correlation or causation IDs.</param>
    /// <param name="deliverAt">An optional future timestamp indicating when the message should become visible for dispatching.</param>
    /// <param name="cancellationToken">A token that can be used to cancel the asynchronous operation.</param>
    /// <returns>A task representing the asynchronous storage operation.</returns>
    ValueTask StoreAsync<TMessage>(
        TMessage message,
        IOutboxTransactionContext transaction,
        OutboxMessageMetadata metadata,
        DateTimeOffset? deliverAt,
        CancellationToken cancellationToken = default) where TMessage : notnull;
}
