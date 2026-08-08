using EricksonLopez.Outbox.Persistence;
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

namespace EricksonLopez.Outbox;

/// <summary>
/// Defines the primary entry point for storing messages in the outbox using an ADO.NET transaction context.
/// </summary>
/// <remarks>
/// <para>
/// <b>Delivery guarantee: At-Least-Once</b><br/>
/// Messages stored via this interface will be delivered to the broker <b>at least once</b>.
/// In crash-recovery scenarios (dispatcher crashes after publishing but before deleting the row),
/// the <c>ReclaimStaleMessagesAsync</c> mechanism will re-surface the message, and it will be
/// published again. This means your message consumers <b>MUST be idempotent</b>.
/// </para>
/// <para>
/// <b>No global ordering guarantee</b><br/>
/// The dispatcher processes messages in creation-time order (<c>ORDER BY created_at ASC, id ASC</c>),
/// but with multiple dispatcher instances running concurrently via <c>SKIP LOCKED</c>, two messages
/// stored in sequence may be published by different instances in a non-deterministic order.
/// If strict ordering is required, use a single dispatcher instance with
/// <c>MaxDegreeOfParallelism = 1</c> and a monotonic ordering key in the message payload.
/// </para>
/// <para>
/// <b>Atomicity</b><br/>
/// All <c>StoreAsync</c> overloads write messages within the caller's <see cref="IOutboxTransactionContext"/>.
/// If the caller's business transaction rolls back, the outbox writes are also rolled back — no messages leak.
/// </para>
/// </remarks>
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
        MessageMetadata metadata,
        DateTimeOffset? deliverAt,
        CancellationToken cancellationToken = default) where TMessage : notnull;

    /// <summary>
    /// Begins a fluent message-building chain for enriching a message before persisting it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>ISSUE-API3 design note (v2.0 candidate):</b> This method returns the concrete type
    /// <see cref="OutboxMessageBuilder{TMessage}"/> directly from the interface, which technically
    /// couples <see cref="IOutbox"/> to the builder implementation. Moving it to a static extension
    /// method outside the interface would improve mockability in tests:
    /// </para>
    /// <code>
    /// public static OutboxMessageBuilder&lt;T&gt; Publish&lt;T&gt;(this IOutbox outbox, T message)
    ///     => new(outbox, message);
    /// </code>
    /// <para>
    /// This change is deferred to v2.0 because it would be a binary breaking change
    /// (removing a method from the interface). In v1.0, consumers can achieve the same testability
    /// benefit by using <see cref="EricksonLopez.Outbox.Testing.InMemoryOutboxStore"/> or
    /// <see cref="EricksonLopez.Outbox.Testing.FakeOutboxDispatcher"/> which do not require mocking the interface.
    /// </para>
    /// </remarks>
    /// <typeparam name="TMessage">The type of the message being built.</typeparam>
    /// <param name="message">The initial message payload to begin enriching.</param>
    /// <returns>A fluent builder instance to configure transaction, delay, and metadata.</returns>
    OutboxMessageBuilder<TMessage> Publish<TMessage>(TMessage message) where TMessage : notnull;
}

/// <summary>
/// Provides convenience extension methods for <see cref="IOutbox"/> to support additional overloads.
/// </summary>
public static class OutboxExtensions
{
    /// <summary>
    /// Stores a sequence of messages atomically within the specified transaction.
    /// </summary>
    /// <remarks>
    /// Internally converts the sequence to <see cref="ReadOnlyMemory{T}"/> using a rented array.
    /// For maximum performance with large batches, prefer the <see cref="IOutbox.StoreAsync{TMessage}(ReadOnlyMemory{TMessage}, IOutboxTransactionContext, CancellationToken)"/>
    /// overload with a pre-allocated buffer.
    /// </remarks>
    /// <typeparam name="TMessage">The type of the messages to store.</typeparam>
    /// <param name="outbox">The outbox instance being extended.</param>
    /// <param name="messages">The sequence of messages to store.</param>
    /// <param name="transaction">The transaction context that scopes this operation.</param>
    /// <param name="cancellationToken">A token that can be used to cancel the asynchronous operation.</param>
    /// <returns>A task representing the asynchronous batch storage operation.</returns>
    public static ValueTask StoreAsync<TMessage>(
        this IOutbox outbox,
        IEnumerable<TMessage> messages,
        IOutboxTransactionContext transaction,
        CancellationToken cancellationToken = default) where TMessage : notnull
    {
        ArgumentNullException.ThrowIfNull(outbox);
        ArgumentNullException.ThrowIfNull(messages);

        // Materialise into an array — unavoidable for ReadOnlyMemory<T> overload.
        // .ToArray() is the least-allocation path for unknown IEnumerable sources.
        var arr = messages is ICollection<TMessage> col
            ? ToArray(col)
            : System.Linq.Enumerable.ToArray(messages);

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
