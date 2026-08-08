using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.Outbox.Persistence;

namespace EricksonLopez.Outbox.Testing;

/// <summary>
/// Provides a non-generic, in-memory implementation of <see cref="IOutboxRepository"/>.
/// </summary>
/// <remarks>
/// This repository simulates a real database-backed repository without performing any I/O operations,
/// making it ideal for testing dispatcher background services and related infrastructure.
/// </remarks>
public sealed class InMemoryOutboxStoreRepository : IOutboxRepository
{
    private readonly ConcurrentDictionary<Guid, OutboxMessage> _pending = new();
    private readonly ConcurrentDictionary<Guid, (OutboxMessage Message, DateTimeOffset FetchedAt)> _inFlight = new();
    private readonly ConcurrentDictionary<Guid, OutboxMessage> _dispatched = new();
    private readonly ConcurrentDictionary<Guid, OutboxMessage> _failed = new();

    /// <inheritdoc/>
    public ValueTask InsertAsync(
        OutboxMessage record,
        IOutboxTransactionContext transaction,
        CancellationToken cancellationToken = default)
    {
        _pending.TryAdd(record.Id, record);
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public ValueTask InsertBatchAsync(
        ReadOnlyMemory<OutboxMessage> records,
        IOutboxTransactionContext transaction,
        CancellationToken cancellationToken = default)
    {
        var span = records.Span;
        for (int i = 0; i < span.Length; i++)
        {
            _pending.TryAdd(span[i].Id, span[i]);
        }
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public ValueTask<IReadOnlyList<OutboxMessage>> FetchPendingAsync(
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var batch = _pending.Values
            .Where(m => m.DeliverAt is null || m.DeliverAt <= now)
            .Take(batchSize)
            .ToList();

        foreach (var m in batch)
        {
            if (_pending.TryRemove(m.Id, out _))
            {
                _inFlight.TryAdd(m.Id, (m, now));
            }
        }

        return new ValueTask<IReadOnlyList<OutboxMessage>>(batch);
    }

    /// <inheritdoc/>
    public ValueTask MarkAsDispatchedAsync(
        IReadOnlyList<OutboxMessage> messages,
        CancellationToken cancellationToken = default)
    {
        foreach (var message in messages)
        {
            if (_inFlight.TryRemove(message.Id, out var entry))
            {
                _dispatched.TryAdd(message.Id, entry.Message);
            }
            else if (_pending.TryRemove(message.Id, out var msg))
            {
                _dispatched.TryAdd(message.Id, msg);
            }
        }
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public ValueTask MarkAsFailedAsync(
        IReadOnlyList<OutboxMessage> messages,
        string error,
        bool isDeadLetter = false,
        CancellationToken cancellationToken = default)
    {
        foreach (var message in messages)
        {
            OutboxMessage? targetMsg = null;
            if (_inFlight.TryRemove(message.Id, out var entry))
            {
                targetMsg = entry.Message;
            }
            else if (_pending.TryRemove(message.Id, out var msg))
            {
                targetMsg = msg;
            }

            if (targetMsg != null)
            {
                if (isDeadLetter)
                {
                    _failed.TryAdd(message.Id, targetMsg);
                }
                else
                {
                    // If it's not a dead letter, it goes back to pending for retry.
                    _pending.TryAdd(message.Id, targetMsg);
                }
            }
        }
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public ValueTask<int> ReclaimStaleMessagesAsync(
        TimeSpan staleTimeout,
        CancellationToken cancellationToken = default)
    {
        var threshold = DateTimeOffset.UtcNow - staleTimeout;
        int count = 0;

        foreach (var kvp in _inFlight)
        {
            if (kvp.Value.FetchedAt < threshold)
            {
                if (_inFlight.TryRemove(kvp.Key, out var entry))
                {
                    _pending.TryAdd(entry.Message.Id, entry.Message);
                    count++;
                }
            }
        }

        return new ValueTask<int>(count);
    }

    /// <inheritdoc/>
    public ValueTask<long> GetPendingCountAsync(CancellationToken cancellationToken = default)
    {
        return new ValueTask<long>(_pending.Count);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// In-memory implementation: searches all state buckets (pending, in-flight, dispatched, failed)
    /// since the InMemory store maintains the full message lifecycle. Returns null if the message
    /// is not found in any bucket.
    /// </remarks>
    public ValueTask<OutboxMessage?> GetMessageAsync(Guid id, CancellationToken cancellationToken = default)
    {
        OutboxMessage? message = null;

        if (_pending.TryGetValue(id, out var pending))
            message = pending;
        else if (_inFlight.TryGetValue(id, out var inFlight))
            message = inFlight.Message;
        else if (_dispatched.TryGetValue(id, out var dispatched))
            message = dispatched;
        else if (_failed.TryGetValue(id, out var failed))
            message = failed;

        return new ValueTask<OutboxMessage?>(message);
    }

    /// <summary>
    /// Retrieves a list of all messages currently residing in the pending queue.
    /// </summary>
    /// <returns>A read-only list of pending outbox messages.</returns>
    public IReadOnlyList<OutboxMessage> GetPending() => _pending.Values.ToList();

    /// <summary>
    /// Retrieves a list of all messages that are currently in-flight (being processed).
    /// </summary>
    /// <returns>A read-only list of in-flight outbox messages.</returns>
    public IReadOnlyList<OutboxMessage> GetInFlight() => _inFlight.Values.Select(v => v.Message).ToList();

    /// <summary>
    /// Retrieves a list of all messages that have been marked as successfully dispatched.
    /// </summary>
    /// <returns>A read-only list of dispatched outbox messages.</returns>
    public IReadOnlyList<OutboxMessage> GetDispatched() => _dispatched.Values.ToList();

    /// <summary>
    /// Retrieves a list of all messages that have been marked as failed.
    /// </summary>
    /// <returns>A read-only list of failed outbox messages.</returns>
    public IReadOnlyList<OutboxMessage> GetFailed() => _failed.Values.ToList();

    /// <summary>
    /// Clears all state across the pending, dispatched, and failed collections.
    /// </summary>
    public void Reset()
    {
        _pending.Clear();
        _inFlight.Clear();
        _dispatched.Clear();
        _failed.Clear();
    }
}
