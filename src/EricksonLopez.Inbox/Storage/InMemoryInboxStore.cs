// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace EricksonLopez.Inbox.Storage;

/// <summary>
/// Provides an in-memory, thread-safe implementation of <see cref="IInboxStore"/> suited for testing and single-node workloads.
/// </summary>
public sealed class InMemoryInboxStore : IInboxStore
{
    private readonly ConcurrentDictionary<(string MessageId, string ConsumerName), DateTimeOffset> _entries = new();
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="InMemoryInboxStore"/> class.
    /// </summary>
    /// <param name="timeProvider">Optional time provider for testability.</param>
    public InMemoryInboxStore(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc/>
    public ValueTask<bool> TryRecordAsync(
        IInboxEntry entry,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(entry.MessageId);
        ArgumentNullException.ThrowIfNull(entry.ConsumerName);

        var key = (entry.MessageId, entry.ConsumerName);
        var inserted = _entries.TryAdd(key, entry.ProcessedAt);
        return ValueTask.FromResult(inserted);
    }

    /// <inheritdoc/>
    public ValueTask<bool> HasBeenProcessedAsync(
        string messageId,
        string consumerName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messageId);
        ArgumentNullException.ThrowIfNull(consumerName);

        var exists = _entries.ContainsKey((messageId, consumerName));
        return ValueTask.FromResult(exists);
    }

    /// <inheritdoc/>
    public ValueTask PurgeExpiredEntriesAsync(
        DateTimeOffset olderThan,
        CancellationToken cancellationToken = default)
    {
        var keysToRemove = new List<(string MessageId, string ConsumerName)>();

        foreach (var kvp in _entries)
        {
            if (kvp.Value < olderThan)
            {
                keysToRemove.Add(kvp.Key);
            }
        }

        foreach (var key in keysToRemove)
        {
            _entries.TryRemove(key, out _);
        }

        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Gets the count of active recorded entries in the store.
    /// </summary>
    public int Count => _entries.Count;

    /// <summary>
    /// Gets the configured <see cref="TimeProvider"/> instance.
    /// </summary>
    public TimeProvider TimeProvider => _timeProvider;

    /// <summary>
    /// Clears all entries from the store.
    /// </summary>
    public void Clear() => _entries.Clear();
}
