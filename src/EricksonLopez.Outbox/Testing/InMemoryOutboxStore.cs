// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.Outbox.Persistence;

namespace EricksonLopez.Outbox.Testing;

/// <summary>
/// Provides a thread-safe, in-memory implementation of <see cref="IOutbox"/> for use in unit tests.
/// </summary>
/// <remarks>
/// This store captures all stored messages in memory, enabling assertions via <see cref="GetPublishedMessages{TMessage}"/>.
/// It avoids the need for mocking frameworks when testing the publisher side of the outbox pattern.
/// </remarks>
public sealed class InMemoryOutboxStore : IOutbox
{
    private readonly ConcurrentQueue<object> _messages = new();

    /// <inheritdoc/>
    public ValueTask StoreAsync<TMessage>(
        TMessage message,
        IOutboxTransactionContext transaction,
        CancellationToken cancellationToken = default) where TMessage : notnull
    {
        _messages.Enqueue(message);
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public ValueTask StoreAsync<TMessage>(
        ReadOnlyMemory<TMessage> messages,
        IOutboxTransactionContext transaction,
        CancellationToken cancellationToken = default) where TMessage : notnull
    {
        var span = messages.Span;
        for (int i = 0; i < span.Length; i++)
        {
            _messages.Enqueue(span[i]);
        }
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public ValueTask StoreAsync<TMessage>(
        TMessage message,
        IOutboxTransactionContext transaction,
        OutboxMessageMetadata metadata,
        DateTimeOffset? deliverAt,
        CancellationToken cancellationToken = default) where TMessage : notnull
    {
        _messages.Enqueue(message);
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Begins a fluent message-building chain for enriching a message before persisting it to this in-memory store.
    /// </summary>
    /// <typeparam name="TMessage">The type of the message being built.</typeparam>
    /// <param name="message">The initial message payload to begin enriching.</param>
    /// <returns>A fluent builder instance to configure transaction, delay, and metadata.</returns>
    public OutboxMessageBuilder<TMessage> Publish<TMessage>(TMessage message) where TMessage : notnull
    {
        return new OutboxMessageBuilder<TMessage>(this, message);
    }

    /// <summary>
    /// Retrieves a read-only list of all stored messages that match the specified type <typeparamref name="TMessage"/>.
    /// </summary>
    /// <typeparam name="TMessage">The type of the messages to retrieve.</typeparam>
    /// <returns>A list containing the matching messages.</returns>
    public IReadOnlyList<TMessage> GetPublishedMessages<TMessage>() where TMessage : notnull
        => _messages.OfType<TMessage>().ToList();

    /// <summary>
    /// Clears all captured messages from the store, resetting its state.
    /// </summary>
    public void Reset()
    {
        _messages.Clear();
    }
}




