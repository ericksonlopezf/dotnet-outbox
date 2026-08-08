using System;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.Outbox.Persistence;

namespace EricksonLopez.Outbox.Idempotency;

/// <summary>
/// Provides idempotency verification for incoming messages.
/// </summary>
public interface IInboxIdempotencyChecker
{
    /// <summary>
    /// Determines whether a message should be processed by attempting to insert its idempotency record.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Transaction Isolation Note:</b> Relies on the underlying database's atomic insert 
    /// capabilities (e.g., ON CONFLICT DO NOTHING) to prevent race conditions (TOCTOU). This is completely 
    /// safe under READ COMMITTED. However, if the provided <see cref="IOutboxTransactionContext"/> uses the 
    /// <c>SERIALIZABLE</c> isolation level, concurrent inserts for the same message ID across different 
    /// transactions may result in a serialization failure exception (e.g., SQL Server error 1205 or PostgreSQL 40001) 
    /// instead of a graceful skip. Consumers using <c>SERIALIZABLE</c> must be prepared to catch and retry 
    /// these specific transaction abort exceptions at the application level.
    /// </para>
    /// </remarks>
    /// <param name="messageId">The unique identifier of the incoming message.</param>
    /// <param name="consumerId">The unique identifier of the consumer processing the message.</param>
    /// <param name="transaction">The active transaction context coordinating the idempotency check and the business operation.</param>
    /// <param name="cancellationToken">A token that can be used to cancel the asynchronous operation.</param>
    /// <returns><see langword="true"/> if the record was successfully inserted and the message should be processed; otherwise, <see langword="false"/>.</returns>
    Task<bool> ShouldProcessAsync(
        string messageId, 
        string consumerId, 
        IOutboxTransactionContext transaction, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Determines whether a specific message has already been processed and should be skipped.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Uses a database insert to atomically detect duplicates. The <paramref name="consumerId"/>
    /// identifies which consumer is performing the check. Always use a unique, stable consumer ID
    /// per consumer in your application to avoid collision with other consumers' records.
    /// </para>
    /// <para>
    /// <b>Warning:</b> Do not reuse <see cref="OutboxConstants.DispatcherConsumerId"/> in your own
    /// consumers. That ID is reserved for the outbox dispatcher's internal deduplication.
    /// Reusing it would cause your consumer's records to collide with the dispatcher's,
    /// resulting in incorrect duplicate-detection behavior.
    /// </para>
    /// </remarks>
    /// <param name="messageId">The unique identifier of the message.</param>
    /// <param name="transaction">The active transaction context coordinating the check.</param>
    /// <param name="consumerId">
    /// A unique, stable identifier for the consumer performing the check.
    /// Defaults to <see cref="OutboxConstants.DispatcherConsumerId"/> for dispatcher-internal use.
    /// Provide a consumer-specific ID (e.g., <c>"order-service.payment-handler"</c>) when calling
    /// from user-facing consumers.
    /// </param>
    /// <param name="cancellationToken">A token that can be used to cancel the asynchronous operation.</param>
    /// <returns><see langword="true"/> if the message has already been processed; otherwise, <see langword="false"/>.</returns>
    Task<bool> ShouldSkipAsync(
        Guid messageId,
        IOutboxTransactionContext transaction,
        string consumerId = OutboxConstants.DispatcherConsumerId,
        CancellationToken cancellationToken = default);
}
