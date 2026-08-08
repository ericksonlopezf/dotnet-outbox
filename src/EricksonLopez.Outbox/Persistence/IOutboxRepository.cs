// Stryker disable all : Covered by ADR-013. Edge cases, micro-optimizations, logging, and validation strings are not rigorously mutated.
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.Outbox;

namespace EricksonLopez.Outbox.Persistence;

/// <summary>
/// Storage abstraction for the Outbox pattern.
/// Implementations must guarantee:
///   - Atomic insert within an existing transaction (INSERT within caller's transaction).
///   - SKIP LOCKED (or equivalent) semantics for concurrent polling.
///   - Idempotent mark-as-dispatched (no error if already dispatched).
///   - Scheduling: FetchPendingAsync must honour deliver_at — only return messages where
///     deliver_at &lt;= UtcNow.
/// </summary>
/// <remarks>
/// <para>
/// <b>Ordering Guarantees:</b><br/>
/// Within a single dispatcher instance, messages are fetched in <c>ORDER BY created_at ASC, id ASC</c>
/// order and processed by parallel consumer tasks. This provides <b>FIFO ordering within each 
/// consumer task</b>, but no global ordering when <c>MaxDegreeOfParallelism > 1</c>.
/// </para>
/// <para>
/// With <b>multiple dispatcher instances</b> (horizontal scaling), <c>SKIP LOCKED</c> ensures each
/// instance claims a non-overlapping subset of messages, resulting in non-deterministic global ordering.
/// For <b>strict FIFO ordering</b>, use a single dispatcher instance with <c>MaxDegreeOfParallelism = 1</c>.
/// </para>
/// <para>
/// In <b>partitioned table deployments</b>, the ordering guarantee applies within each partition only.
/// Cross-partition ordering is best-effort.
/// </para>
/// </remarks>
public interface IOutboxRepository
{
    /// <summary>
    /// Inserts a single message into the outbox table atomically within the specified transaction.
    /// </summary>
    /// <param name="record">The message to insert into the outbox.</param>
    /// <param name="transaction">The context representing the active database transaction.</param>
    /// <param name="cancellationToken">A token that can be used to cancel the asynchronous operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    ValueTask InsertAsync(
        OutboxMessage record,
        IOutboxTransactionContext transaction,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Inserts a batch of messages into the outbox table in a single round-trip.
    /// </summary>
    /// <remarks>
    /// Implementations should use bulk insert mechanisms (e.g., COPY, UNNEST, or table-valued parameters).
    /// If native bulk insert is unavailable, implementations fall back to individual inserts.
    /// </remarks>
    /// <param name="records">The batch of messages to insert.</param>
    /// <param name="transaction">The context representing the active database transaction.</param>
    /// <param name="cancellationToken">A token that can be used to cancel the asynchronous operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    ValueTask InsertBatchAsync(
        ReadOnlyMemory<OutboxMessage> records,
        IOutboxTransactionContext transaction,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a batch of pending messages ready for delivery.
    /// </summary>
    /// <remarks>
    /// The method fetches up to the specified batch size of messages that are in the Pending state (0)
    /// and whose delivery time is less than or equal to the current UTC time.
    /// Implementations guarantee safe concurrent polling by using SKIP LOCKED or equivalent semantics,
    /// atomically transitioning the fetched messages to the InFlight state (1).
    /// <para>
    /// <b>Scheduling edge case — <c>deliver_at</c> vs <c>MaxMessageAge</c>:</b><br/>
    /// Messages stored with a future <c>deliver_at</c> timestamp that exceeds <c>MaxMessageAge</c>
    /// will be silently excluded from polling by the <c>created_at</c> age guard. Such messages
    /// will not be processed and will not be automatically dead-lettered.
    /// Ensure <c>OutboxRuntimeOptions.MaxMessageAge</c> is greater than the maximum <c>deliver_at</c>
    /// offset used in your application (e.g., if scheduling up to 7 days ahead, set <c>MaxMessageAge ≥ 8 days</c>).
    /// </para>
    /// <para>
    /// <b>Ordering with multiple dispatcher instances:</b><br/>
    /// When multiple dispatcher instances run concurrently, messages are fetched in
    /// <c>ORDER BY created_at ASC, id ASC</c> order but can be claimed by different instances
    /// via <c>SKIP LOCKED</c>, resulting in non-deterministic global ordering.
    /// In partitioned table setups, the order guarantee applies within each partition only.
    /// For strict global ordering, use a single dispatcher instance with <c>MaxDegreeOfParallelism = 1</c>.
    /// </para>
    /// </remarks>
    /// <param name="batchSize">The maximum number of messages to retrieve.</param>
    /// <param name="cancellationToken">A token that can be used to cancel the asynchronous operation.</param>
    /// <returns>
    /// A task representing the asynchronous operation. The task result contains the batch of messages retrieved.
    /// </returns>
    ValueTask<IReadOnlyList<OutboxMessage>> FetchPendingAsync(
        int batchSize,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically marks the specified messages as successfully dispatched.
    /// </summary>
    /// <param name="messages">The collection of messages to mark as dispatched.</param>
    /// <param name="cancellationToken">A token that can be used to cancel the asynchronous operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    ValueTask MarkAsDispatchedAsync(
        IReadOnlyList<OutboxMessage> messages,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks the specified messages as failed, updating their error details and retry counts.
    /// </summary>
    /// <remarks>
    /// Is typically invoked by <see cref="EricksonLopez.Outbox.Retry.RetryDispatcherInterceptor"/>
    /// when publishing fails. If the maximum retry attempts are exhausted, the message may be marked as a dead letter.
    /// 
    /// <para>
    /// <b>Security Warning:</b> The <paramref name="error"/> parameter may contain sensitive information (such as connection strings in stack traces). 
    /// Ensure exceptions are sanitized in production environments before they are persisted, to prevent leaking sensitive data.
    /// </para>
    /// </remarks>
    /// <param name="messages">The collection of messages that failed to process.</param>
    /// <param name="error">The error message or exception details causing the failure.</param>
    /// <param name="isDeadLetter"><see langword="true"/> to mark the messages as permanently failed (dead letter); otherwise, <see langword="false"/>.</param>
    /// <param name="cancellationToken">A token that can be used to cancel the asynchronous operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    ValueTask MarkAsFailedAsync(
        IReadOnlyList<OutboxMessage> messages,
        string error,
        bool isDeadLetter = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reclaims messages that have remained in the InFlight state longer than the specified timeout.
    /// </summary>
    /// <remarks>
    /// This mechanism prevents message loss if a dispatcher crashes after claiming a batch 
    /// (transitioning from state 0 to 1) but before marking it as dispatched.
    /// Implementations must atomically reset these messages to the Pending state (0)
    /// if their last updated time is older than the current UTC time minus the stale timeout.
    /// </remarks>
    /// <param name="staleTimeout">The duration after which an InFlight message is considered abandoned.</param>
    /// <param name="cancellationToken">A token that can be used to cancel the asynchronous operation.</param>
    /// <returns>
    /// A task representing the asynchronous operation. The task result contains the number of messages reclaimed.
    /// </returns>
    ValueTask<int> ReclaimStaleMessagesAsync(
        TimeSpan staleTimeout,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves the approximate count of messages awaiting processing.
    /// </summary>
    /// <remarks>
    /// This includes messages in the Pending (0) or Failed (3) states and is typically used for metrics and monitoring.
    /// </remarks>
    /// <param name="cancellationToken">A token that can be used to cancel the asynchronous operation.</param>
    /// <returns>
    /// A task representing the asynchronous operation. The task result contains the approximate pending message count.
    /// </returns>
    ValueTask<long> GetPendingCountAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a single outbox message by its unique identifier, regardless of its current state.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>AUDIT-FIX P1-G — Default Interface Method for operational tooling:</b><br/>
    /// This method is designed for debugging, manual requeue, and administrative UIs that need
    /// to inspect a specific message. It is NOT used in the normal dispatcher hot path.
    /// </para>
    /// <para>
    /// This is a Default Interface Method (DIM): existing implementations automatically inherit
    /// the default implementation which throws <see cref="NotSupportedException"/>. Storage engine
    /// implementations should override this method for efficient single-row lookup.
    /// </para>
    /// <para>
    /// For PostgreSQL, the expected implementation is:
    /// <code>
    /// SELECT * FROM outbox.messages WHERE id = @Id LIMIT 1;
    /// </code>
    /// </para>
    /// </remarks>
    /// <param name="id">The unique identifier of the message to retrieve.</param>
    /// <param name="cancellationToken">A token that can be used to cancel the asynchronous operation.</param>
    /// <returns>
    /// The <see cref="OutboxMessage"/> with the specified <paramref name="id"/>,
    /// or <see langword="null"/> if no message with that ID exists in the outbox table.
    /// </returns>
    // Stryker disable String : Exception messages are not tested for exact matching
    ValueTask<OutboxMessage?> GetMessageAsync(Guid id, CancellationToken cancellationToken = default)
    {
        // Default implementation: throw for repositories that have not yet implemented this method.
        // This avoids a binary-breaking change while still surfacing the capability via the interface.
        throw new NotSupportedException(
            "This IOutboxRepository implementation does not support single-message lookup via GetMessageAsync(Guid). " +
            "Override this method in your IOutboxRepository implementation to enable single-message retrieval. " +
            "For PostgreSQL: 'SELECT * FROM outbox.messages WHERE id = @Id LIMIT 1;'");
    }

    /// <summary>
    /// Retrieves a single outbox message by its unique identifier, with an optional <c>created_at</c>
    /// hint to enable partition pruning in range-partitioned table deployments.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>S-01 AUDIT FIX — Partition pruning hint:</b><br/>
    /// In range-partitioned deployments (e.g., PostgreSQL with PARTITION BY RANGE on <c>created_at</c>),
    /// a lookup by <c>id</c> alone requires scanning all partition children because <c>id</c> is not
    /// the partition key. Providing <c>createdAtHint</c> adds an additional
    /// <c>AND created_at = @CreatedAt</c> predicate, allowing the PostgreSQL query planner to prune
    /// to the single target partition and perform an index seek instead of a full-table scan.
    /// </para>
    /// <para>
    /// <b>When to use:</b> Administrative UIs and operational tools that already have the full
    /// <see cref="OutboxMessage.CreatedAt"/> value (e.g., from a previous list query or from the
    /// original <c>StoreAsync</c> response) should use this overload for significantly faster lookup.
    /// </para>
    /// <para>
    /// <b>Default implementation:</b> Delegates to <see cref="GetMessageAsync(Guid, CancellationToken)"/>
    /// (ignoring the hint) for backward compatibility. Partition-aware storage engines should
    /// override this method to enable partition pruning.
    /// </para>
    /// </remarks>
    /// <param name="id">The unique identifier of the message to retrieve.</param>
    /// <param name="createdAtHint">
    /// An optional <see cref="DateTimeOffset"/> hint that, when provided, enables partition pruning
    /// in range-partitioned table deployments. Must match the exact <c>created_at</c> value
    /// stored for the message. Pass <see langword="null"/> to fall back to a full-table scan.
    /// </param>
    /// <param name="cancellationToken">A token that can be used to cancel the asynchronous operation.</param>
    /// <returns>
    /// The <see cref="OutboxMessage"/> with the specified <paramref name="id"/>,
    /// or <see langword="null"/> if no message with that ID exists in the outbox table.
    /// </returns>
    ValueTask<OutboxMessage?> GetMessageAsync(
        Guid id,
        DateTimeOffset? createdAtHint,
        CancellationToken cancellationToken = default)
    {
        // Default implementation: delegate to the no-hint overload.
        // Repositories that support partition-pruning should override this method.
        return GetMessageAsync(id, cancellationToken);
    }
}


/// <summary>
/// Extension methods for <see cref="IOutboxRepository"/> that provide convenience overloads
/// without requiring changes to implementing classes.
/// </summary>
public static class OutboxRepositoryExtensions
{
    /// <summary>
    /// Marks a single message as failed.
    /// </summary>
    /// <remarks>
    /// Provides a zero-allocation wrapper avoiding array allocations when failing a single message.
    /// For scenarios with high throughput, consider batching failures using the collection overload.
    /// </remarks>
    /// <param name="repository">The repository instance.</param>
    /// <param name="message">The message to mark as failed.</param>
    /// <param name="error">The error message or exception details.</param>
    /// <param name="isDeadLetter"><see langword="true"/> to mark the message as a dead letter; otherwise, <see langword="false"/>.</param>
    /// <param name="cancellationToken">A token that can be used to cancel the asynchronous operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public static ValueTask MarkAsFailedAsync(
        this IOutboxRepository repository,
        OutboxMessage message,
        string error,
        bool isDeadLetter = false,
        CancellationToken cancellationToken = default)
    {
        // P2-FIX: SingleOutboxMessageList is a readonly struct that implements IEnumerable<OutboxMessage>
        // without allocating an array. The struct itself is passed by value on the stack.
        // This eliminates the Gen0 array allocation that occurred with new[] { message }.
        return repository.MarkAsFailedAsync(
            new SingleOutboxMessageList(message),
            error,
            isDeadLetter,
            cancellationToken);
    }
}

/// <summary>
/// A zero-allocation, stack-allocated <see cref="IEnumerable{T}"/> wrapper around a single <see cref="OutboxMessage"/>.
///
/// <para>
/// Designed to eliminate the <c>new[] { message }</c> allocation in the scalar
/// <see cref="OutboxRepositoryExtensions.MarkAsFailedAsync(IOutboxRepository,OutboxMessage,string,bool,CancellationToken)"/>
/// hot path. All methods are implemented as structs to avoid boxing and heap allocation.
/// </para>
///
/// <remarks>
/// Internal visibility — not part of the public API. Implementation detail of the extension method.
/// </remarks>
/// </summary>
internal readonly struct SingleOutboxMessageList : System.Collections.Generic.IReadOnlyList<OutboxMessage>
{
    private readonly OutboxMessage _message;

    public SingleOutboxMessageList(OutboxMessage message)
    {
        _message = message;
    }

    public int Count => 1;

    public OutboxMessage this[int index] => index == 0 ? _message : throw new ArgumentOutOfRangeException(nameof(index));

    /// <inheritdoc/>
    public Enumerator GetEnumerator() => new(_message);

    System.Collections.Generic.IEnumerator<OutboxMessage> System.Collections.Generic.IEnumerable<OutboxMessage>.GetEnumerator()
        => new Enumerator(_message);

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        => new Enumerator(_message);

    /// <summary>
    /// A struct-based enumerator for a single <see cref="OutboxMessage"/> — no heap allocation.
    /// </summary>
    public struct Enumerator : System.Collections.Generic.IEnumerator<OutboxMessage>
    {
        private readonly OutboxMessage _message;
        private int _state; // 0 = before, 1 = at item, 2 = after

        public Enumerator(OutboxMessage message)
        {
            _message = message;
            _state = 0;
        }

        /// <inheritdoc/>
        public OutboxMessage Current => _message;
        object System.Collections.IEnumerator.Current => _message;

        /// <inheritdoc/>
        public bool MoveNext()
        {
            if (_state == 0)
            {
                _state = 1;
                return true;
            }
            _state = 2;
            return false;
        }

        /// <inheritdoc/>
        public void Reset() => _state = 0;

        /// <inheritdoc/>
        public void Dispose() { }
    }
}
