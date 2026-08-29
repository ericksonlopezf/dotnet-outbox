// Copyright © Erickson Lopez. MIT License.
using System;
using EricksonLopez.Outbox;

namespace EricksonLopez.Outbox.Serialization.Protobuf;

/// <summary>
/// Provides extension methods for configuring Protocol Buffers serialization for the outbox.
/// </summary>
public static class ProtobufOutboxSerializationExtensions
{
    /// <summary>
    /// Configures the outbox to use Protocol Buffers (<see cref="ProtobufOutboxSerializer"/>) for payload serialization.
    /// </summary>
    /// <param name="options">The outbox options builder.</param>
    /// <returns>The outbox options builder for method chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    public static OutboxOptions UseProtobufSerializer(this OutboxOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.UseSerializer(new ProtobufOutboxSerializer());
        return options;
    }
}

