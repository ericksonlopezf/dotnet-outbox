// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;

namespace EricksonLopez.Outbox;


/// <summary>
/// Represents key-value metadata associated with an Outbox message, optimized for raw persistence and retrieval from a data store.
/// </summary>
/// <remarks>
/// This type is a persistence-oriented representation of metadata and is intentionally distinct from
/// <c>EricksonLopez.Messaging.Contracts.TransportMessageMetadata</c>, which models strongly typed metadata
/// for distributed message routing and transport.
/// </remarks>
public readonly struct OutboxMessageMetadata
{
    private readonly ReadOnlyMemory<MetadataEntry> _entries;

    /// <summary>
    /// Gets the correlation identifier used to trace a logical operation across multiple services.
    /// </summary>
    public string? CorrelationId { get; }

    /// <summary>
    /// Gets the causation identifier that links this message directly to the message or operation that triggered it.
    /// </summary>
    public string? CausationId { get; }

    /// <summary>
    /// Gets the string alias representing the type of the message payload.
    /// </summary>
    public string? MessageType { get; }

    /// <summary>
    /// Gets the read-only memory segment of custom metadata entries (headers).
    /// </summary>
    public ReadOnlyMemory<MetadataEntry> Entries => _entries;

    /// <summary>
    /// Initializes a new instance of the <see cref="OutboxMessageMetadata"/> struct.
    /// </summary>
    /// <param name="correlationId">The correlation identifier.</param>
    /// <param name="causationId">The causation identifier.</param>
    /// <param name="messageType">The explicit message type alias, if any.</param>
    /// <param name="entries">A memory segment of custom metadata entries.</param>
    public OutboxMessageMetadata(
        string? correlationId,
        string? causationId,
        string? messageType,
        ReadOnlyMemory<MetadataEntry> entries = default)
    {
        CorrelationId = correlationId;
        CausationId = causationId;
        MessageType = messageType;
        _entries = entries;
    }

    /// <summary>
    /// Retrieves the value of a metadata entry by its key.
    /// </summary>
    /// <param name="key">The exact, case-sensitive key to search for.</param>
    /// <returns>The value associated with the specified key, or <see langword="null"/> if the key is not found.</returns>
    public string? GetValue(string key)
    {
        if (_entries.IsEmpty)
            return null;

        var span = _entries.Span;
        for (int i = 0; i < span.Length; i++)
        {
            if (string.Equals(span[i].Key, key, StringComparison.Ordinal))
            {
                return span[i].Value;
            }
        }

        return null;
    }
}

