// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.Outbox;
using EricksonLopez.Result;

namespace EricksonLopez.Outbox.Persistence;

/// <summary>
/// Defines a contract for dead-letter queue storage operations.
/// </summary>
/// <remarks>
/// A message is dead-lettered when it has exhausted all configured retry attempts
/// without being successfully dispatched to the broker.
///
/// Dead-lettered messages are stored separately from the main outbox table to:
///   1. Prevent them from being re-fetched by the dispatcher indefinitely.
///   2. Allow human inspection, manual replay, or automated discard.
///   3. Provide a durable audit trail of permanently failed messages.
///
/// Implementations must guarantee:
///   - Atomic insert within the caller's transaction (or a separate connection).
///   - The original OutboxMessage ID is preserved in OriginalMessageId for correlation.
///   - Graceful handling of transaction=default. The dispatcher frequently attempts to insert dead letters outside of a transaction context. If transaction is null, the repository MUST open its own connection and auto-commit the insert.
/// </remarks>
public interface IDeadLetterRepository
{
    /// <summary>
    /// Persists a dead-lettered message.
    /// </summary>
    /// <remarks>
    /// Called by the Dispatcher after a message has exhausted its <see cref="EricksonLopez.Outbox.Retry.RetryPolicy"/>.
    /// </remarks>
    /// <param name="message">The dead-lettered message to persist.</param>
    /// <param name="transaction">The optional transaction context. If <see langword="null"/>, the repository must open its own connection and auto-commit the insert.</param>
    /// <param name="cancellationToken">A token that can be used to cancel the asynchronous operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    ValueTask InsertAsync(
        DeadLetterMessage message,
        EricksonLopez.Outbox.Persistence.IOutboxTransactionContext? transaction = default,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a page of dead-lettered messages for inspection or replay.
    /// </summary>
    /// <param name="limit">The maximum number of records to return.</param>
    /// <param name="after">The cursor indicating to return records dead-lettered after this timestamp. Pass <see cref="DateTimeOffset.MinValue"/> for the first page.</param>
    /// <param name="cancellationToken">A token that can be used to cancel the asynchronous operation.</param>
    /// <returns>
    /// A task representing the asynchronous operation. The task result contains the retrieved page of dead-lettered messages.
    /// </returns>
    ValueTask<IReadOnlyList<DeadLetterMessage>> GetAsync(
        int limit = 100,
        DateTimeOffset? after = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a dead-lettered message by its unique identifier.
    /// </summary>
    /// <param name="id">The unique identifier of the dead-lettered message to delete.</param>
    /// <param name="cancellationToken">A token that can be used to cancel the asynchronous operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    ValueTask DeleteAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Purges all dead-lettered messages older than the specified timestamp.
    /// </summary>
    /// <param name="olderThan">The timestamp threshold; messages older than this value will be purged.</param>
    /// <param name="cancellationToken">A token that can be used to cancel the asynchronous operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    ValueTask PurgeAsync(
        DateTimeOffset olderThan,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a value indicating whether this is a first-party (built-in) implementation.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Used by <c>OutboxStartupValidator</c> to warn operators when a third-party
    /// <see cref="IDeadLetterRepository"/> is registered, reminding them to verify that their
    /// implementation correctly handles <c>transaction = null</c> (auto-commit mode).
    /// </para>
    /// <para>
    /// All first-party storage engine implementations override this to return <see langword="true"/>.
    /// Third-party implementations that do not override this will return <see langword="false"/>,
    /// triggering the startup advisory log message.
    /// </para>
    /// <para>
    /// This is a Zero-Reflection pattern: no <c>GetType().Name</c>, no string comparisons,
    /// no assembly scanning. The check is a simple virtual dispatch.
    /// </para>
    /// </remarks>
    bool IsFirstPartyImplementation => false;
}




