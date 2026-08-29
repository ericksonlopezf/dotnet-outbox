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
