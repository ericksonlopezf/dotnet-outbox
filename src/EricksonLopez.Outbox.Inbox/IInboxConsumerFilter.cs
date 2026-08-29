// Copyright © Erickson Lopez. MIT License.
using System;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.Inbox;
using EricksonLopez.Outbox.Persistence;

namespace EricksonLopez.Outbox.Inbox;

/// <summary>
/// Defines an abstraction for consumer-side message deduplication and idempotent handler execution with optional transaction context.
/// </summary>
public interface IInboxConsumerFilter : EricksonLopez.Inbox.IInboxConsumerFilter
{
    /// <summary>
    /// Executes the given handler if and only if the message with <paramref name="messageId"/> has not yet been processed by <paramref name="consumerName"/>.
    /// </summary>
    /// <param name="messageId">The unique incoming message identifier.</param>
    /// <param name="consumerName">The logical consumer or handler name.</param>
    /// <param name="handler">The message processing delegate.</param>
    /// <param name="transaction">The optional database transaction context.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns><see langword="true"/> if the message was new and handled successfully; <see langword="false"/> if it was a duplicate and skipped.</returns>
    ValueTask<bool> ExecuteIdempotentlyAsync(
        string messageId,
        string consumerName,
        Func<CancellationToken, ValueTask> handler,
        IOutboxTransactionContext? transaction = null,
        CancellationToken cancellationToken = default);

    /// <inheritdoc/>
    ValueTask<bool> EricksonLopez.Inbox.IInboxConsumerFilter.ExecuteIdempotentlyAsync(
        string messageId,
        string consumerName,
        Func<CancellationToken, ValueTask> handler,
        CancellationToken cancellationToken) =>
        ExecuteIdempotentlyAsync(messageId, consumerName, handler, transaction: null, cancellationToken: cancellationToken);
}
