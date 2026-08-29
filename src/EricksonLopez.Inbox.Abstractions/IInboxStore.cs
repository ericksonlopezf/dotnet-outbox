// Copyright © Erickson Lopez. MIT License.
using System;
using System.Threading;
using System.Threading.Tasks;

namespace EricksonLopez.Inbox;

/// <summary>
/// Defines persistence operations for storing, querying, and purging consumer inbox entries.
/// </summary>
public interface IInboxStore
{
    /// <summary>
    /// Attempts to record a processed message entry.
    /// </summary>
    /// <param name="entry">The inbox entry to record.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>
    /// A task representing the asynchronous operation. The result is <see langword="true"/> if the entry was newly recorded;
    /// otherwise <see langword="false"/> if it already exists (duplicate).
    /// </returns>
    ValueTask<bool> TryRecordAsync(
        IInboxEntry entry,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks whether the message has already been processed by the specified consumer.
    /// </summary>
    /// <param name="messageId">The message identifier.</param>
    /// <param name="consumerName">The consumer name.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns><see langword="true"/> if already processed; otherwise, <see langword="false"/>.</returns>
    ValueTask<bool> HasBeenProcessedAsync(
        string messageId,
        string consumerName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Purges inbox entries older than the specified timestamp threshold.
    /// </summary>
    /// <param name="olderThan">The timestamp threshold; entries older than this will be purged.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A <see cref="ValueTask"/> representing the asynchronous operation.</returns>
    ValueTask PurgeExpiredEntriesAsync(
        DateTimeOffset olderThan,
        CancellationToken cancellationToken = default);
}
