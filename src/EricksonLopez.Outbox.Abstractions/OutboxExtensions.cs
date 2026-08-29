// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.Outbox.Persistence;

namespace EricksonLopez.Outbox;

/// <summary>
/// Provides convenience extension methods for <see cref="IOutbox"/> to support additional overloads.
/// </summary>
public static class OutboxExtensions
{
    /// <summary>
    /// Stores a sequence of messages atomically within the specified transaction.
    /// </summary>
    /// <typeparam name="TMessage">The type of the messages to store.</typeparam>
    /// <param name="outbox">The outbox instance.</param>
    /// <param name="messages">The sequence of messages to store in the outbox.</param>
    /// <param name="transaction">The transaction context that scopes this operation.</param>
    /// <param name="cancellationToken">A token that can be used to cancel the asynchronous operation.</param>
    /// <returns>A task representing the asynchronous batch storage operation.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="outbox"/> or <paramref name="messages"/> is <see langword="null"/>.</exception>
    public static ValueTask StoreAsync<TMessage>(
        this IOutbox outbox,
        IEnumerable<TMessage> messages,
        IOutboxTransactionContext transaction,
        CancellationToken cancellationToken = default) where TMessage : notnull
    {
        ArgumentNullException.ThrowIfNull(outbox);
        ArgumentNullException.ThrowIfNull(messages);

        var arr = messages is ICollection<TMessage> col
            ? ToArray(col)
            : Enumerable.ToArray(messages);

        if (arr.Length == 0)
            return ValueTask.CompletedTask;

        return outbox.StoreAsync<TMessage>(new ReadOnlyMemory<TMessage>(arr), transaction, cancellationToken);
    }

    private static TMessage[] ToArray<TMessage>(ICollection<TMessage> source)
    {
        var arr = new TMessage[source.Count];
        source.CopyTo(arr, 0);
        return arr;
    }
}
