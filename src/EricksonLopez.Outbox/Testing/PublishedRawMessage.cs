// Copyright © Erickson Lopez. MIT License.
using System;

namespace EricksonLopez.Outbox.Testing;

/// <summary>
/// Represents a raw message that was published and captured by the fake broker.
/// </summary>
/// <param name="MessageType">The string alias of the message type.</param>
/// <param name="Payload">The raw serialized payload of the message.</param>
/// <param name="Metadata">The metadata associated with the message.</param>
public sealed record PublishedRawMessage(
    string MessageType,
    ReadOnlyMemory<byte> Payload,
    OutboxMessageMetadata Metadata);
