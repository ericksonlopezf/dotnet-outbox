using System;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.Outbox.Idempotency;
using EricksonLopez.Outbox.Persistence;

namespace EricksonLopez.Outbox.Testing;

/// <summary>
/// A fake <see cref="IInboxIdempotencyChecker"/> implementation designed for use in unit and integration tests.
/// </summary>
/// <remarks>
/// This fake implementation delegates to a provided <see cref="FakeIdempotencyRepository"/>
/// to simulate exactly-once processing behavior without requiring a real database connection.
/// </remarks>
public sealed class FakeInboxIdempotencyChecker : IInboxIdempotencyChecker
{
    private readonly FakeIdempotencyRepository _repository;

    /// <summary>
    /// Initializes a new instance of the <see cref="FakeInboxIdempotencyChecker"/> class.
    /// </summary>
    /// <param name="repository">The fake repository that stores and verify idempotency records.</param>
    public FakeInboxIdempotencyChecker(FakeIdempotencyRepository repository)
    {
        _repository = repository;
    }

    /// <inheritdoc />
    public async Task<bool> ShouldProcessAsync(
        string messageId, 
        string consumerId, 
        IOutboxTransactionContext transaction, 
        CancellationToken cancellationToken = default)
    {
        var record = new EricksonLopez.Outbox.IdempotencyRecord(messageId, consumerId, DateTimeOffset.UtcNow);
        return await _repository.TryInsertAsync(record, transaction, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> ShouldSkipAsync(
        Guid messageId,
        IOutboxTransactionContext transaction,
        string consumerId = OutboxConstants.DispatcherConsumerId,
        CancellationToken cancellationToken = default)
    {
        var record = new EricksonLopez.Outbox.IdempotencyRecord(messageId.ToString(), consumerId, DateTimeOffset.UtcNow);
        var inserted = await _repository.TryInsertAsync(record, transaction, cancellationToken).ConfigureAwait(false);
        return !inserted;
    }
}
