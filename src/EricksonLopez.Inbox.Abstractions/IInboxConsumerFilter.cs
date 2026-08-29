// Copyright © Erickson Lopez. MIT License.
using System;
using System.Threading;
using System.Threading.Tasks;

namespace EricksonLopez.Inbox;

/// <summary>
/// Defines the contract for consumer-side message deduplication and idempotent handler execution.
/// </summary>
public interface IInboxConsumerFilter
{
    /// <summary>
    /// Executes the given handler if and only if the message with <paramref name="messageId"/> has not yet been processed by <paramref name="consumerName"/>.
    /// </summary>
    /// <param name="messageId">The unique incoming message identifier.</param>
    /// <param name="consumerName">The logical consumer or handler name.</param>
    /// <param name="handler">The message processing delegate.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns><see langword="true"/> if the message was new and handled successfully; <see langword="false"/> if it was a duplicate and skipped.</returns>
    ValueTask<bool> ExecuteIdempotentlyAsync(
        string messageId,
        string consumerName,
        Func<CancellationToken, ValueTask> handler,
        CancellationToken cancellationToken = default);
}
