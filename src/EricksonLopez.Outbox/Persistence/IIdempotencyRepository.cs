using System;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.Outbox;

namespace EricksonLopez.Outbox.Persistence;

/// <summary>
/// Storage abstraction for the Inbox idempotency pattern.
/// </summary>
/// <remarks>
/// Implementations must guarantee atomic insert-or-ignore semantics.
/// </remarks>
public interface IIdempotencyRepository
{
    /// <summary>
    /// Attempts to insert an idempotency record.
    /// </summary>
    /// <param name="record">The idempotency record to insert.</param>
    /// <param name="transaction">The optional transaction context.</param>
    /// <param name="cancellationToken">A token that can be used to cancel the asynchronous operation.</param>
    /// <returns>
    /// A task representing the asynchronous operation. The task result is <see langword="true"/> if the record was inserted;
    /// otherwise, <see langword="false"/> if the record already exists (duplicate).
    /// </returns>
    ValueTask<bool> TryInsertAsync(
        IdempotencyRecord record,
        EricksonLopez.Outbox.Persistence.IOutboxTransactionContext? transaction = default,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Purges idempotency records older than the specified timestamp.
    /// </summary>
    /// <remarks>
    /// Called periodically by <c>InboxCleanupService</c> to prevent table bloat.
    /// </remarks>
    /// <param name="olderThan">The timestamp threshold; records older than this value will be purged.</param>
    /// <param name="cancellationToken">A token that can be used to cancel the asynchronous operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    ValueTask PurgeExpiredRecordsAsync(
        DateTimeOffset olderThan,
        CancellationToken cancellationToken = default);
}
