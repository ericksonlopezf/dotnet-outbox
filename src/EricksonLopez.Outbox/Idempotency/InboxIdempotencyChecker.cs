// Copyright © Erickson Lopez. MIT License.
using System;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.Outbox;
using EricksonLopez.Outbox.Persistence;

namespace EricksonLopez.Outbox.Idempotency;

/// <summary>
/// Provides idempotency verification for incoming messages using the configured <see cref="IIdempotencyRepository"/>.
/// </summary>
/// <remarks>
/// This checker is used to ensure exactly-once processing semantics by rejecting messages
/// that have already been processed by a specific consumer.
/// </remarks>
public sealed class InboxIdempotencyChecker : IInboxIdempotencyChecker
{
    private readonly IIdempotencyRepository _idempotencyRepository;

    /// <summary>
    /// Initializes a new instance of the <see cref="InboxIdempotencyChecker"/> class.
    /// </summary>
    /// <param name="idempotencyRepository">The repository that stores and verify idempotency records.</param>
    public InboxIdempotencyChecker(IIdempotencyRepository idempotencyRepository)
    {
        ArgumentNullException.ThrowIfNull(idempotencyRepository);
        _idempotencyRepository = idempotencyRepository;
    }

    /// <summary>
    /// Determines whether a message should be processed by attempting to insert its idempotency record.
    /// </summary>
    /// <param name="messageId">The unique identifier of the incoming message.</param>
    /// <param name="consumerId">The unique identifier of the consumer processing the message.</param>
    /// <param name="transaction">The active transaction context coordinating the idempotency check and the business operation.</param>
    /// <param name="cancellationToken">A token that can be used to cancel the asynchronous operation.</param>
    /// <returns><see langword="true"/> if the record was successfully inserted and the message should be processed; otherwise, <see langword="false"/>.</returns>
    public async Task<bool> ShouldProcessAsync(
        string messageId,
        string consumerId,
        IOutboxTransactionContext transaction,
        CancellationToken cancellationToken = default)
    {
        var record = new IdempotencyRecord(messageId, consumerId, DateTimeOffset.UtcNow);
        return await _idempotencyRepository.TryInsertAsync(record, transaction, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Determines whether a specific message has already been processed and should be skipped.
    /// </summary>
    /// <param name="messageId">The unique identifier of the message.</param>
    /// <param name="transaction">The active transaction context coordinating the check.</param>
    /// <param name="consumerId">
    /// The consumer identifier used to scope the idempotency check.
    /// Defaults to <see cref="OutboxConstants.DispatcherConsumerId"/> for dispatcher-internal use.
    /// </param>
    /// <param name="cancellationToken">A token that can be used to cancel the asynchronous operation.</param>
    /// <returns><see langword="true"/> if the message has already been processed; otherwise, <see langword="false"/>.</returns>
    public async Task<bool> ShouldSkipAsync(
        Guid messageId,
        IOutboxTransactionContext transaction,
        string consumerId = OutboxConstants.DispatcherConsumerId,
        CancellationToken cancellationToken = default)
    {
        // ISSUE-C1 FIX: Use the caller-provided consumerId instead of the hardcoded
        // "outbox-dispatcher" string. The interface default ensures the dispatcher's
        // own internal calls continue to work without any changes at the call site.
        var record = new IdempotencyRecord(messageId.ToString(), consumerId, DateTimeOffset.UtcNow);
        var inserted = await _idempotencyRepository.TryInsertAsync(record, transaction, cancellationToken).ConfigureAwait(false);
        return !inserted;
    }
}



