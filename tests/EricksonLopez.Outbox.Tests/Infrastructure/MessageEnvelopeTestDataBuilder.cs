// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.Linq;
using EricksonLopez.Outbox;

namespace EricksonLopez.Outbox.Tests.Infrastructure;

/// <summary>
/// Fluent test data builder for <see cref="MessageEnvelope{T}"/> with sensible defaults.
/// </summary>
/// <typeparam name="T">The payload type.</typeparam>
public sealed class MessageEnvelopeTestDataBuilder<T> where T : notnull
{
    private T _payload;
    private string? _correlationId = "test-corr-id";
    private string? _causationId = "test-caus-id";
    private string? _messageType = typeof(T).Name;
    private readonly List<MetadataEntry> _customMetadata = new();

    public MessageEnvelopeTestDataBuilder(T defaultPayload)
    {
        _payload = defaultPayload;
    }

    public MessageEnvelopeTestDataBuilder<T> WithPayload(T payload)
    {
        _payload = payload;
        return this;
    }

    public MessageEnvelopeTestDataBuilder<T> WithCorrelationId(string? correlationId)
    {
        _correlationId = correlationId;
        return this;
    }

    public MessageEnvelopeTestDataBuilder<T> WithCausationId(string? causationId)
    {
        _causationId = causationId;
        return this;
    }

    public MessageEnvelopeTestDataBuilder<T> WithMessageType(string? messageType)
    {
        _messageType = messageType;
        return this;
    }

    public MessageEnvelopeTestDataBuilder<T> WithMetadataEntry(string key, string value)
    {
        _customMetadata.Add(new MetadataEntry(key, value));
        return this;
    }

    public MessageEnvelope<T> Build()
    {
        var metadata = new OutboxMessageMetadata(
            _correlationId,
            _causationId,
            _messageType,
            _customMetadata.Count > 0 ? _customMetadata.ToArray() : null);

        return new MessageEnvelope<T>(_payload, metadata);
    }

    public static implicit operator MessageEnvelope<T>(MessageEnvelopeTestDataBuilder<T> builder) => builder.Build();
}

/// <summary>
/// Static helper factory for <see cref="MessageEnvelopeTestDataBuilder{T}"/>.
/// </summary>
public static class MessageEnvelopeTestDataBuilder
{
    public static MessageEnvelopeTestDataBuilder<T> Create<T>(T payload) where T : notnull
    {
        return new MessageEnvelopeTestDataBuilder<T>(payload);
    }

    public static MessageEnvelopeTestDataBuilder<string> CreateDefault()
    {
        return new MessageEnvelopeTestDataBuilder<string>("test-payload-data");
    }
}



