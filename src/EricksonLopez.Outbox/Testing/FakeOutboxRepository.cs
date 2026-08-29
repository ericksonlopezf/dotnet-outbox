// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.Outbox.Persistence;

namespace EricksonLopez.Outbox.Testing;

/// <summary>
/// Provides a test double for <see cref="IOutboxRepository"/> that stores outbox messages in memory.
/// </summary>
/// <remarks>
/// <para>
/// This type is a discoverable, named test double following the established <c>Fake*</c> naming convention
/// alongside <see cref="FakeBrokerPublisher"/>, <see cref="FakeDeadLetterRepository"/>,
/// and <see cref="FakeIdempotencyRepository"/>.
/// </para>
/// <para>
/// Use this type when testing dispatcher background services, retry logic, or any component that
/// depends on <see cref="IOutboxRepository"/> directly. For testing the <see cref="IOutbox"/> producer
/// side, prefer <see cref="InMemoryOutboxStore"/> instead.
/// </para>
/// <para>
/// <b>State inspection:</b> Access <see cref="Inner"/> to inspect pending, in-flight,
/// dispatched, and failed message collections after exercising the dispatcher under test.
/// </para>
/// <example>
/// <code>
/// var fakeRepo = new FakeOutboxRepository();
/// services.AddSingleton&lt;IOutboxRepository&gt;(fakeRepo);
///
/// // After test:
/// var pending = fakeRepo.Inner.GetPending();
/// Assert.Equal(0, pending.Count);
/// </code>
/// </example>
/// </remarks>
public sealed class FakeOutboxRepository : IOutboxRepository
{
    // InMemoryOutboxStoreRepository is sealed, so we use composition.
    // Expose Inner so test code can call GetPending(), GetDispatched(), GetFailed(), Reset().
    private readonly InMemoryOutboxStoreRepository _inner = new();

    /// <summary>Gets the underlying <see cref="InMemoryOutboxStoreRepository"/> for state inspection in tests.</summary>
    public InMemoryOutboxStoreRepository Inner => _inner;

    /// <inheritdoc />
    public ValueTask InsertAsync(OutboxMessage record, IOutboxTransactionContext transaction, CancellationToken cancellationToken = default)
        => _inner.InsertAsync(record, transaction, cancellationToken);

    /// <inheritdoc />
    public ValueTask InsertBatchAsync(ReadOnlyMemory<OutboxMessage> records, IOutboxTransactionContext transaction, CancellationToken cancellationToken = default)
        => _inner.InsertBatchAsync(records, transaction, cancellationToken);

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<OutboxMessage>> FetchPendingAsync(int batchSize, CancellationToken cancellationToken = default)
        => _inner.FetchPendingAsync(batchSize, cancellationToken);

    /// <inheritdoc />
    public ValueTask MarkAsDispatchedAsync(IReadOnlyList<OutboxMessage> messages, CancellationToken cancellationToken = default)
        => _inner.MarkAsDispatchedAsync(messages, cancellationToken);

    /// <inheritdoc />
    public ValueTask MarkAsFailedAsync(IReadOnlyList<OutboxMessage> messages, string error, bool isDeadLetter = false, CancellationToken cancellationToken = default)
        => _inner.MarkAsFailedAsync(messages, error, isDeadLetter, cancellationToken);

    /// <inheritdoc />
    public ValueTask<int> ReclaimStaleMessagesAsync(TimeSpan staleTimeout, CancellationToken cancellationToken = default)
        => _inner.ReclaimStaleMessagesAsync(staleTimeout, cancellationToken);

    /// <inheritdoc />
    public ValueTask<long> GetPendingCountAsync(CancellationToken cancellationToken = default)
        => _inner.GetPendingCountAsync(cancellationToken);

    /// <inheritdoc />
    public ValueTask<OutboxMessage?> GetMessageAsync(Guid id, CancellationToken cancellationToken = default)
        => _inner.GetMessageAsync(id, cancellationToken);

    /// <inheritdoc />
    public ValueTask<int> PurgeDispatchedMessagesAsync(DateTimeOffset cutoff, int batchSize = 1000, CancellationToken cancellationToken = default)
        => _inner.PurgeDispatchedMessagesAsync(cutoff, batchSize, cancellationToken);
}






