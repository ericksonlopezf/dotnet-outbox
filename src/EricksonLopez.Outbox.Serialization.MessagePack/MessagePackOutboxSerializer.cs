// Copyright © Erickson Lopez. MIT License.
using System;
using System.Buffers;
using System.Linq;
using EricksonLopez.Outbox.Serialization;
using MessagePack;

namespace EricksonLopez.Outbox.Serialization.MessagePack;

/// <summary>
/// Provides a high-performance, compact binary serializer implementation using MessagePack.
/// </summary>
public sealed class MessagePackOutboxSerializer : IOutboxSerializer
{
    private readonly MessagePackSerializerOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="MessagePackOutboxSerializer"/> class.
    /// </summary>
    /// <param name="options">Optional MessagePack serializer options (defaults to <see cref="MessagePackSerializerOptions.Standard"/>).</param>
    public MessagePackOutboxSerializer(MessagePackSerializerOptions? options = null)
    {
        _options = options ?? MessagePackSerializerOptions.Standard;
    }

    /// <inheritdoc/>
    public ReadOnlyMemory<byte> Serialize<TMessage>(TMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        return MessagePackSerializer.Serialize(message, _options);
    }

    /// <inheritdoc/>
    public void Serialize<TMessage>(TMessage message, IBufferWriter<byte> buffer)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(buffer);

        MessagePackSerializer.Serialize(buffer, message, _options);
    }

    /// <inheritdoc/>
    public TMessage Deserialize<TMessage>(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
        {
            throw new ArgumentException("Data to deserialize cannot be empty.", nameof(data));
        }

        return MessagePackSerializer.Deserialize<TMessage>(data.ToArray(), _options);
    }
}


