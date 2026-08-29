// Copyright © Erickson Lopez. MIT License.
using System;
using EricksonLopez.Outbox;

namespace EricksonLopez.Outbox.Tests.Infrastructure;

/// <summary>
/// Fluent test data builder for <see cref="OutboxMessage"/> with sensible defaults.
/// </summary>
public sealed class OutboxMessageTestDataBuilder
{
    private Guid _id = Guid.NewGuid();
    private string _messageType = "test.message.v1";
    private ReadOnlyMemory<byte> _payload = new byte[] { 1, 2, 3 };
    private string? _correlationId = "test-corr-id";
    private string? _causationId = "test-caus-id";
    private ReadOnlyMemory<byte> _headers = System.Text.Encoding.UTF8.GetBytes("{}");
    private DateTimeOffset _createdAt = DateTimeOffset.UtcNow;
    private DateTimeOffset? _processedAt;
    private DateTimeOffset? _deliverAt;
    private OutboxMessageStatus _status = OutboxMessageStatus.Pending;
    private int _retryCount;
    private string? _error;

    public OutboxMessageTestDataBuilder WithId(Guid id) { _id = id; return this; }
    public OutboxMessageTestDataBuilder WithMessageType(string messageType) { _messageType = messageType; return this; }
    public OutboxMessageTestDataBuilder WithPayload(byte[] payload) { _payload = payload; return this; }
    public OutboxMessageTestDataBuilder WithPayload(ReadOnlyMemory<byte> payload) { _payload = payload; return this; }
    public OutboxMessageTestDataBuilder WithCorrelationId(string? correlationId) { _correlationId = correlationId; return this; }
    public OutboxMessageTestDataBuilder WithCausationId(string? causationId) { _causationId = causationId; return this; }
    public OutboxMessageTestDataBuilder WithHeaders(byte[] headers) { _headers = headers; return this; }
    public OutboxMessageTestDataBuilder WithHeaders(ReadOnlyMemory<byte> headers) { _headers = headers; return this; }
    public OutboxMessageTestDataBuilder WithHeadersJson(string jsonHeaders) { _headers = System.Text.Encoding.UTF8.GetBytes(jsonHeaders); return this; }
    public OutboxMessageTestDataBuilder WithCreatedAt(DateTimeOffset createdAt) { _createdAt = createdAt; return this; }
    public OutboxMessageTestDataBuilder WithProcessedAt(DateTimeOffset? processedAt) { _processedAt = processedAt; return this; }
    public OutboxMessageTestDataBuilder WithDeliverAt(DateTimeOffset? deliverAt) { _deliverAt = deliverAt; return this; }
    public OutboxMessageTestDataBuilder WithStatus(OutboxMessageStatus status) { _status = status; return this; }
    public OutboxMessageTestDataBuilder WithRetryCount(int retryCount) { _retryCount = retryCount; return this; }
    public OutboxMessageTestDataBuilder WithError(string? error) { _error = error; return this; }

    public OutboxMessage Build()
    {
        return new OutboxMessage(
            _id,
            _messageType,
            _payload,
            _correlationId,
            _causationId,
            _headers,
            _createdAt,
            _processedAt,
            _deliverAt,
            _status,
            _retryCount,
            _error);
    }

    public static implicit operator OutboxMessage(OutboxMessageTestDataBuilder builder) => builder.Build();
}
