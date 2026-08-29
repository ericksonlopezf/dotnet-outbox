// Copyright © Erickson Lopez. MIT License.
using System;
using System.Threading;
using System.Threading.Tasks;

namespace EricksonLopez.Inbox;

/// <summary>
/// Provides idempotency verification and execution guarantees for incoming messages.
/// </summary>
public interface IIdempotencyChecker
{
    /// <summary>
    /// Checks whether the message identified by <paramref name="messageId"/> has already been processed by <paramref name="consumerName"/>.
    /// </summary>
    /// <param name="messageId">The message identifier.</param>
    /// <param name="consumerName">The consumer or handler name.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns><see langword="true"/> if already processed; otherwise, <see langword="false"/>.</returns>
    ValueTask<bool> HasProcessedAsync(
        string messageId,
        string consumerName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes the specified handler if and only if the message has not yet been processed by <paramref name="consumerName"/>.
    /// </summary>
    /// <param name="messageId">The message identifier.</param>
    /// <param name="consumerName">The consumer or handler name.</param>
    /// <param name="handler">The message processing delegate.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns><see langword="true"/> if the message was new and executed; <see langword="false"/> if it was a duplicate and skipped.</returns>
    ValueTask<bool> ExecuteIdempotentlyAsync(
        string messageId,
        string consumerName,
        Func<CancellationToken, ValueTask> handler,
        CancellationToken cancellationToken = default);
}
