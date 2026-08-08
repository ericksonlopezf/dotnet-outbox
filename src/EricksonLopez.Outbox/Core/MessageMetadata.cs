using System;
using System.Collections.Generic;


namespace EricksonLopez.Outbox;

/// <summary>
/// Represents a single key-value pair of metadata associated with a message.
/// </summary>
/// <param name="Key">The key identifying the metadata entry.</param>
/// <param name="Value">The value of the metadata entry.</param>
public readonly record struct MetadataEntry(string Key, string Value);

/// <summary>
/// Encapsulates the metadata associated with a message, including tracing identifiers and custom headers.
/// </summary>
public readonly struct MessageMetadata
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
    /// Initializes a new instance of the <see cref="MessageMetadata"/> struct.
    /// </summary>
    /// <param name="correlationId">The correlation identifier.</param>
    /// <param name="causationId">The causation identifier.</param>
    /// <param name="messageType">The explicit message type alias, if any.</param>
    /// <param name="entries">A memory segment of custom metadata entries.</param>
    public MessageMetadata(
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
    /// <remarks>
    /// Uses a linear search, which is highly efficient for the typical case of a small number of headers (e.g., &lt; 10) due to CPU cache locality.
    /// </remarks>
    /// <param name="key">The exact, case-sensitive key to search for.</param>
    /// <returns>The value associated with the specified key, or <see langword="null"/> if the key is not found.</returns>
    public string? GetValue(string key)
    {
        if (_entries.IsEmpty)
            return null;

        var span = _entries.Span;
        // Linear search is faster for small collections (typically < 10 headers) due to L1 cache
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
