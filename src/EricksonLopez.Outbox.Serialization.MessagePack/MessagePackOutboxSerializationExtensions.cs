// Copyright © Erickson Lopez. MIT License.
using System;
using EricksonLopez.Outbox;
using MessagePack;

namespace EricksonLopez.Outbox.Serialization.MessagePack;

/// <summary>
/// Provides extension methods for configuring MessagePack serialization for the outbox.
/// </summary>
public static class MessagePackOutboxSerializationExtensions
{
    /// <summary>
    /// Configures the outbox to use MessagePack (<see cref="MessagePackOutboxSerializer"/>) for payload serialization.
    /// </summary>
    /// <param name="options">The outbox options builder.</param>
    /// <param name="messagePackOptions">Optional custom MessagePack serialization options.</param>
    /// <returns>The outbox options builder for method chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    public static OutboxOptions UseMessagePackSerializer(
        this OutboxOptions options,
        MessagePackSerializerOptions? messagePackOptions = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.UseSerializer(new MessagePackOutboxSerializer(messagePackOptions));
        return options;
    }
}

