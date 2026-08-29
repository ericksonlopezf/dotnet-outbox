// Copyright © Erickson Lopez. MIT License.
using System;
using System.Text;
using AwesomeAssertions;
using EricksonLopez.Outbox.EntityFrameworkCore.Entities;
using Xunit;

namespace EricksonLopez.Outbox.EntityFrameworkCore.Tests;

public class EntityTests
{
    [Fact]
    public void IdempotencyRecordEntity_Defaults_ShouldBeExpected()
    {
        var entity = new IdempotencyRecordEntity();
        entity.MessageId.Should().BeEmpty();
        entity.ConsumerId.Should().BeEmpty();
        entity.ProcessedAt.Should().Be(default);
    }

    [Fact]
    public void IdempotencyRecordEntity_FromModel_And_ToModel_Roundtrip()
    {
        var now = DateTimeOffset.UtcNow;
        var model = new IdempotencyRecord("msg-123", "consumer-abc", now);

        var entity = IdempotencyRecordEntity.FromModel(model);
        entity.MessageId.Should().Be("msg-123");
        entity.ConsumerId.Should().Be("consumer-abc");
        entity.ProcessedAt.Should().Be(now);

        var roundtrip = entity.ToModel();
        roundtrip.MessageId.Should().Be("msg-123");
        roundtrip.ConsumerId.Should().Be("consumer-abc");
        roundtrip.ProcessedAt.Should().Be(now);
    }

    [Fact]
    public void OutboxMessageEntity_Defaults_ShouldBeExpected()
    {
        var entity = new OutboxMessageEntity();
        entity.Id.Should().Be(Guid.Empty);
        entity.MessageType.Should().BeEmpty();
        entity.Payload.Should().BeEmpty();
        entity.CorrelationId.Should().BeNull();
        entity.CausationId.Should().BeNull();
        entity.HeadersJson.Should().Be("{}");
        entity.CreatedAt.Should().Be(default);
        entity.ProcessedAt.Should().BeNull();
        entity.DeliverAt.Should().BeNull();
        entity.State.Should().Be(0);
        entity.RetryCount.Should().Be(0);
        entity.Error.Should().BeNull();
    }

    [Fact]
    public void OutboxMessageEntity_FromModel_And_ToModel_Roundtrip()
    {
        var id = Guid.NewGuid();
        var createdAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        var processedAt = DateTimeOffset.UtcNow;
        var deliverAt = DateTimeOffset.UtcNow.AddMinutes(10);
        var payload = Encoding.UTF8.GetBytes("{\"test\":\"data\"}");
        var headers = Encoding.UTF8.GetBytes("{\"header1\":\"val1\"}");

        var model = new OutboxMessage(
            Id: id,
            MessageType: "OrderPlaced",
            Payload: payload,
            CorrelationId: "corr-1",
            CausationId: "caus-1",
            Headers: headers,
            CreatedAt: createdAt,
            ProcessedAt: processedAt,
            DeliverAt: deliverAt,
            Status: OutboxMessageStatus.InFlight,
            RetryCount: 3,
            Error: "Some transient error");

        var entity = OutboxMessageEntity.FromModel(model);
        entity.Id.Should().Be(id);
        entity.MessageType.Should().Be("OrderPlaced");
        entity.Payload.Should().Equal(payload);
        entity.CorrelationId.Should().Be("corr-1");
        entity.CausationId.Should().Be("caus-1");
        entity.HeadersJson.Should().Be("{\"header1\":\"val1\"}");
        entity.CreatedAt.Should().Be(createdAt);
        entity.ProcessedAt.Should().Be(processedAt);
        entity.DeliverAt.Should().Be(deliverAt);
        entity.State.Should().Be((int)OutboxMessageStatus.InFlight);
        entity.RetryCount.Should().Be(3);
        entity.Error.Should().Be("Some transient error");

        var roundtrip = entity.ToModel();
        roundtrip.Id.Should().Be(id);
        roundtrip.MessageType.Should().Be("OrderPlaced");
        roundtrip.Payload.ToArray().Should().Equal(payload);
        roundtrip.CorrelationId.Should().Be("corr-1");
        roundtrip.CausationId.Should().Be("caus-1");
        Encoding.UTF8.GetString(roundtrip.Headers.Span).Should().Be("{\"header1\":\"val1\"}");
        roundtrip.CreatedAt.Should().Be(createdAt);
        roundtrip.ProcessedAt.Should().Be(processedAt);
        roundtrip.DeliverAt.Should().Be(deliverAt);
        roundtrip.Status.Should().Be(OutboxMessageStatus.InFlight);
        roundtrip.RetryCount.Should().Be(3);
        roundtrip.Error.Should().Be("Some transient error");
    }

    [Fact]
    public void OutboxMessageEntity_ToModel_ShouldHandleNullHeadersJson()
    {
        var entity = new OutboxMessageEntity
        {
            Id = Guid.NewGuid(),
            MessageType = "TestMessage",
            Payload = Array.Empty<byte>(),
            HeadersJson = null!, // Simulate null from DB
            CreatedAt = DateTimeOffset.UtcNow,
            State = (int)OutboxMessageStatus.Pending,
            RetryCount = 0
        };

        var model = entity.ToModel();

        Encoding.UTF8.GetString(model.Headers.Span).Should().Be("{}");
    }

    [Fact]
    public void OutboxMessageEntity_ToModel_ShouldHandleNonNullHeadersJson()
    {
        var entity = new OutboxMessageEntity
        {
            Id = Guid.NewGuid(),
            MessageType = "TestMessage",
            Payload = Array.Empty<byte>(),
            HeadersJson = "{\"Key\":\"Value\"}",
            CreatedAt = DateTimeOffset.UtcNow,
            State = (int)OutboxMessageStatus.Pending,
            RetryCount = 0
        };

        var model = entity.ToModel();

        Encoding.UTF8.GetString(model.Headers.Span).Should().Be("{\"Key\":\"Value\"}");
    }

    [Fact]
    public void DeadLetterMessageEntity_Defaults_ShouldBeExpected()
    {
        var entity = new DeadLetterMessageEntity();
        entity.Id.Should().Be(Guid.Empty);
        entity.OriginalMessageId.Should().Be(Guid.Empty);
        entity.MessageType.Should().BeEmpty();
        entity.Payload.Should().BeEmpty();
        entity.CorrelationId.Should().BeNull();
        entity.CausationId.Should().BeNull();
        entity.HeadersJson.Should().Be("{}");
        entity.CreatedAt.Should().Be(default);
        entity.DeadLetteredAt.Should().Be(default);
        entity.RetryCount.Should().Be(0);
        entity.Reason.Should().BeEmpty();
        entity.LastError.Should().BeNull();
    }

    [Fact]
    public void DeadLetterMessageEntity_FromModel_And_ToModel_Roundtrip()
    {
        var id = Guid.NewGuid();
        var origId = Guid.NewGuid();
        var createdAt = DateTimeOffset.UtcNow.AddMinutes(-10);
        var deadLetteredAt = DateTimeOffset.UtcNow;
        var payload = Encoding.UTF8.GetBytes("{\"dlq\":\"payload\"}");
        var headers = Encoding.UTF8.GetBytes("{\"dlq-header\":\"v\"}");

        var model = new DeadLetterMessage(
            Id: id,
            OriginalMessageId: origId,
            MessageType: "OrderFailed",
            Payload: payload,
            CorrelationId: "corr-dlq",
            CausationId: "caus-dlq",
            Headers: headers,
            CreatedAt: createdAt,
            DeadLetteredAt: deadLetteredAt,
            RetryCount: 5,
            Reason: "MaxRetriesExceeded",
            LastError: "Connection refused");

        var entity = DeadLetterMessageEntity.FromModel(model);
        entity.Id.Should().Be(id);
        entity.OriginalMessageId.Should().Be(origId);
        entity.MessageType.Should().Be("OrderFailed");
        entity.Payload.Should().Equal(payload);
        entity.CorrelationId.Should().Be("corr-dlq");
        entity.CausationId.Should().Be("caus-dlq");
        entity.HeadersJson.Should().Be("{\"dlq-header\":\"v\"}");
        entity.CreatedAt.Should().Be(createdAt);
        entity.DeadLetteredAt.Should().Be(deadLetteredAt);
        entity.RetryCount.Should().Be(5);
        entity.Reason.Should().Be("MaxRetriesExceeded");
        entity.LastError.Should().Be("Connection refused");

        var roundtrip = entity.ToModel();
        roundtrip.Id.Should().Be(id);
        roundtrip.OriginalMessageId.Should().Be(origId);
        roundtrip.MessageType.Should().Be("OrderFailed");
        roundtrip.Payload.ToArray().Should().Equal(payload);
        roundtrip.CorrelationId.Should().Be("corr-dlq");
        roundtrip.CausationId.Should().Be("caus-dlq");
        Encoding.UTF8.GetString(roundtrip.Headers.Span).Should().Be("{\"dlq-header\":\"v\"}");
        roundtrip.CreatedAt.Should().Be(createdAt);
        roundtrip.DeadLetteredAt.Should().Be(deadLetteredAt);
        roundtrip.RetryCount.Should().Be(5);
        roundtrip.Reason.Should().Be("MaxRetriesExceeded");
        roundtrip.LastError.Should().Be("Connection refused");
    }

    [Fact]
    public void DeadLetterMessageEntity_ToModel_ShouldHandleNullHeadersJson()
    {
        var entity = new DeadLetterMessageEntity
        {
            Id = Guid.NewGuid(),
            OriginalMessageId = Guid.NewGuid(),
            MessageType = "TestMessage",
            Payload = Array.Empty<byte>(),
            HeadersJson = null!, // Simulate null from DB
            CreatedAt = DateTimeOffset.UtcNow,
            DeadLetteredAt = DateTimeOffset.UtcNow,
            RetryCount = 0
        };

        var model = entity.ToModel();

        Encoding.UTF8.GetString(model.Headers.Span).Should().Be("{}");
    }

    [Fact]
    public void DeadLetterMessageEntity_ToModel_ShouldHandleNonNullHeadersJson()
    {
        var entity = new DeadLetterMessageEntity
        {
            Id = Guid.NewGuid(),
            OriginalMessageId = Guid.NewGuid(),
            MessageType = "TestMessage",
            Payload = Array.Empty<byte>(),
            HeadersJson = "{\"Key\":\"Value\"}",
            CreatedAt = DateTimeOffset.UtcNow,
            DeadLetteredAt = DateTimeOffset.UtcNow,
            RetryCount = 0
        };

        var model = entity.ToModel();

        Encoding.UTF8.GetString(model.Headers.Span).Should().Be("{\"Key\":\"Value\"}");
    }
}
