// Copyright © Erickson Lopez. MIT License.
using System;
using System.Buffers;

namespace EricksonLopez.Outbox.Serialization;

/// <summary>
/// Defines an abstraction for serializing outbox messages before they are stored in the database or dispatched over the network.
/// </summary>
public interface IOutboxSerializer
{
    /// <summary>
    /// Serializes the specified message payload into a byte array payload.
    /// Exposes a <see cref="ReadOnlyMemory{T}"/> to avoid defensive array copying where possible.
    /// </summary>
    /// <typeparam name="TMessage">The type of the message being serialized.</typeparam>
    /// <param name="message">The message to serialize.</param>
    /// <returns>A read-only memory region containing the UTF-8 encoded bytes of the serialized message.</returns>
    ReadOnlyMemory<byte> Serialize<TMessage>(TMessage message);

    /// <summary>
    /// Serializes the specified message directly into the provided buffer writer.
    /// </summary>
    /// <remarks>
    /// Avoids heap allocations for intermediate byte arrays by writing directly to the buffer.
    /// Implementations should override it for maximum throughput on hot paths. The default implementation
    /// serializes into an intermediate byte array.
    /// </remarks>
    /// <typeparam name="TMessage">The type of the message being serialized.</typeparam>
    /// <param name="message">The message to serialize.</param>
    /// <param name="buffer">The buffer writer to which the serialized message bytes are written.</param>
    void Serialize<TMessage>(TMessage message, IBufferWriter<byte> buffer)
    {
        // Default: delegate to the allocating overload. Implementors should override
        // this with a direct IBufferWriter write (e.g. JsonSerializer.Serialize(buffer, ...)).
        var bytes = Serialize(message);
        var span = buffer.GetSpan(bytes.Length);
        bytes.Span.CopyTo(span);
        buffer.Advance(bytes.Length);
    }

    /// <summary>
    /// Deserializes a message of the specified type from a UTF-8 byte span.
    /// </summary>
    /// <typeparam name="TMessage">The expected type of the deserialized message.</typeparam>
    /// <param name="data">The read-only span of bytes containing the serialized message data.</param>
    /// <returns>The deserialized message instance.</returns>
    TMessage Deserialize<TMessage>(ReadOnlySpan<byte> data);
}
