// Copyright © Erickson Lopez. MIT License.
using System;

namespace EricksonLopez.Outbox;

/// <summary>
/// Wraps a strongly-typed message payload along with its associated metadata for publishing.
/// </summary>
/// <typeparam name="T">The type of the message payload being enveloped.</typeparam>
/// <param name="Payload">The strongly-typed message payload.</param>
/// <param name="Metadata">The metadata associated with the message, such as headers and tracing identifiers.</param>
public readonly record struct MessageEnvelope<T>(T Payload, OutboxMessageMetadata Metadata) where T : notnull;



