// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.Outbox;
using EricksonLopez.Outbox.Persistence;
using EricksonLopez.Result;

namespace EricksonLopez.Outbox.Testing;

/// <summary>
/// Provides an in-memory implementation of <see cref="IDeadLetterRepository"/> for use in unit and integration tests.
/// </summary>
/// <remarks>
/// This implementation is thread-safe, utilizing a lock on its internal collection to support concurrent test scenarios.
/// </remarks>
public sealed class FakeDeadLetterRepository : IDeadLetterRepository
{
    private readonly List<DeadLetterMessage> _messages = new();
    private readonly object _syncRoot = new();

    /// <summary>
    /// Gets a snapshot of all dead-lettered messages currently stored in the repository.
    /// </summary>
    public IReadOnlyList<DeadLetterMessage> Messages
    {
        get
        {
            lock (_syncRoot)
            {
                return _messages.ToList().AsReadOnly();
            }
        }
    }

    /// <summary>
    /// Gets the current count of dead-lettered messages in the repository.
    /// </summary>
    public int Count
    {
        get
        {
            lock (_syncRoot) { return _messages.Count; }
        }
    }

    /// <inheritdoc/>
    public bool IsFirstPartyImplementation => true;

    /// <summary>
    /// Clears all messages from the repository. Useful for resetting state between test cases.
    /// </summary>
    public void Clear()
    {
        lock (_syncRoot) { _messages.Clear(); }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Gracefully handles <paramref name="transaction"/> being null, as required by the contract.
    /// </remarks>
    public ValueTask InsertAsync(
        DeadLetterMessage message,
        IOutboxTransactionContext? transaction = default,
        CancellationToken cancellationToken = default)
    {
        lock (_syncRoot)
        {
            _messages.Add(message);
        }
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public ValueTask<IReadOnlyList<DeadLetterMessage>> GetAsync(
        int limit = 100,
        DateTimeOffset? after = null,
        CancellationToken cancellationToken = default)
    {
        lock (_syncRoot)
        {
            var query = _messages.AsEnumerable();
            if (after.HasValue)
                query = query.Where(m => m.DeadLetteredAt > after.Value);

            IReadOnlyList<DeadLetterMessage> result = query
                .OrderBy(m => m.DeadLetteredAt)
                .Take(limit)
                .ToList()
                .AsReadOnly();

            return ValueTask.FromResult(result);
        }
    }

    /// <inheritdoc/>
    public ValueTask DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        lock (_syncRoot)
        {
            _messages.RemoveAll(m => m.Id == id);
        }
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public ValueTask PurgeAsync(DateTimeOffset olderThan, CancellationToken cancellationToken = default)
    {
        lock (_syncRoot)
        {
            _messages.RemoveAll(m => m.DeadLetteredAt < olderThan);
        }
        return ValueTask.CompletedTask;
    }
}




