// Copyright © Erickson Lopez. MIT License.
using System;
using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using EricksonLopez.Outbox.Serialization;
using ProtoBuf;

namespace EricksonLopez.Outbox.Serialization.Protobuf;

/// <summary>
/// Provides a high-performance, compact binary serializer implementation using Protocol Buffers (protobuf-net).
/// </summary>
[UnconditionalSuppressMessage("Trimming", "IL2091", Justification = "protobuf-net uses runtime type model or source-generated serializers")]
[UnconditionalSuppressMessage("AOT", "IL3050", Justification = "protobuf-net uses runtime type model or source-generated serializers")]
public sealed class ProtobufOutboxSerializer : IOutboxSerializer
{
    /// <inheritdoc/>
    [UnconditionalSuppressMessage("Trimming", "IL2091", Justification = "protobuf-net uses runtime type model")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "protobuf-net uses runtime type model")]
    public ReadOnlyMemory<byte> Serialize<TMessage>(TMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        using var stream = new MemoryStream();
        Serializer.Serialize(stream, message);
        return stream.ToArray();
    }

    /// <inheritdoc/>
    [UnconditionalSuppressMessage("Trimming", "IL2091", Justification = "protobuf-net uses runtime type model")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "protobuf-net uses runtime type model")]
    public void Serialize<TMessage>(TMessage message, IBufferWriter<byte> buffer)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(buffer);

        using var stream = new MemoryStream();
        Serializer.Serialize(stream, message);
        var span = buffer.GetSpan((int)stream.Length);
        stream.GetBuffer().AsSpan(0, (int)stream.Length).CopyTo(span);
        buffer.Advance((int)stream.Length);
    }

    /// <inheritdoc/>
    [UnconditionalSuppressMessage("Trimming", "IL2091", Justification = "protobuf-net uses runtime type model")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "protobuf-net uses runtime type model")]
    public TMessage Deserialize<TMessage>(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
        {
            throw new ArgumentException("Data to deserialize cannot be empty.", nameof(data));
        }

        using var stream = new MemoryStream(data.ToArray());
        return Serializer.Deserialize<TMessage>(stream);
    }
}


