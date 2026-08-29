// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.Outbox;
using EricksonLopez.Outbox.Persistence;

namespace EricksonLopez.Outbox.Testing;

/// <summary>
/// Provides an in-memory implementation of <see cref="IIdempotencyRepository"/> for use in unit and integration tests.
/// </summary>
/// <remarks>
/// <para>
/// This implementation allows testing of consumers that use <c>InboxIdempotencyChecker</c> without requiring a real database.
/// It uses a <see cref="ConcurrentDictionary{TKey,TValue}"/> keyed by <c>{messageId}:{consumerId}</c> to guarantee
/// thread-safe, atomic insert-or-ignore semantics — matching the database-level <c>ON CONFLICT DO NOTHING</c> behavior.
/// </para>
/// <para>
/// <b>Usage example:</b>
/// <code>
/// // In your test setup:
/// var fakeRepo = new FakeIdempotencyRepository();
/// var checker = new InboxIdempotencyChecker(fakeRepo);
///
/// // First call evaluates to true (should process):
/// var shouldProcess = await checker.ShouldProcessAsync("msg-1", "consumer-A", tx, ct);
/// Assert.True(shouldProcess);
///
/// // Second call evaluates to false (duplicate):
/// var duplicate = await checker.ShouldProcessAsync("msg-1", "consumer-A", tx, ct);
/// Assert.False(duplicate);
///
/// // Assert what was recorded:
/// Assert.Single(fakeRepo.Records);
/// </code>
/// </para>
/// </remarks>
public sealed class FakeIdempotencyRepository : IIdempotencyRepository
{
    // Key: "{messageId}:{consumerId}" — mirrors the composite unique constraint in real DB implementations.
    private readonly ConcurrentDictionary<string, IdempotencyRecord> _records = new(StringComparer.Ordinal);

    /// <summary>
    /// Gets a snapshot of all idempotency records currently stored in the repository.
    /// </summary>
    public ICollection<IdempotencyRecord> Records => _records.Values;

    /// <summary>
    /// Gets the current count of recorded idempotency entries.
    /// </summary>
    public int Count => _records.Count;

    /// <summary>
    /// Clears all records from the repository. Useful for resetting state between test cases.
    /// </summary>
    public void Clear() => _records.Clear();

    /// <summary>
    /// Determines whether the specified message ID and consumer ID pair has already been processed.
    /// </summary>
    /// <param name="messageId">The unique identifier of the message.</param>
    /// <param name="consumerId">The unique identifier of the consumer.</param>
    /// <returns><see langword="true"/> if a record exists for the given pair; otherwise, <see langword="false"/>.</returns>
    public bool WasProcessed(string messageId, string consumerId)
        => _records.ContainsKey(BuildKey(messageId, consumerId));

    /// <inheritdoc/>
    /// <remarks>
    /// Uses <see cref="ConcurrentDictionary{TKey,TValue}.TryAdd"/> for atomic insert-or-ignore semantics.
    /// The <paramref name="transaction"/> parameter is ignored in this in-memory implementation.
    /// </remarks>
    public ValueTask<bool> TryInsertAsync(
        IdempotencyRecord record,
        IOutboxTransactionContext? transaction = default,
        CancellationToken cancellationToken = default)
    {
        var key = BuildKey(record.MessageId, record.ConsumerId);
        var inserted = _records.TryAdd(key, record);
        return ValueTask.FromResult(inserted);
    }

    /// <inheritdoc/>
    /// <remarks>Removes all records whose <see cref="IdempotencyRecord.ProcessedAt"/> is older than <paramref name="olderThan"/>.</remarks>
    public ValueTask PurgeExpiredRecordsAsync(
        DateTimeOffset olderThan,
        CancellationToken cancellationToken = default)
    {
        foreach (var kv in _records)
        {
            if (kv.Value.ProcessedAt < olderThan)
                _records.TryRemove(kv.Key, out _);
        }
        return ValueTask.CompletedTask;
    }

    private static string BuildKey(string messageId, string consumerId)
        => $"{messageId}:{consumerId}";
}




