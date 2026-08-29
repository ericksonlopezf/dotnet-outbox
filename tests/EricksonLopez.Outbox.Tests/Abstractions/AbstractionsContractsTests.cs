// Copyright © Erickson Lopez. MIT License.
using System;
using AwesomeAssertions;
using EricksonLopez.Outbox;
using EricksonLopez.Outbox.Contracts;
using EricksonLopez.Outbox.Persistence;
using NSubstitute;
using Xunit;

namespace EricksonLopez.Outbox.Tests.Abstractions;

public class AbstractionsContractsTests
{
    [Fact]
    public void MetadataEntry_PropertiesAndEquality_WorkCorrectly()
    {
        var entry1 = new MetadataEntry("X-Trace-Id", "12345");
        var entry2 = new MetadataEntry("X-Trace-Id", "12345");
        var entry3 = new MetadataEntry("X-Span-Id", "67890");

        entry1.Key.Should().Be("X-Trace-Id");
        entry1.Value.Should().Be("12345");
        entry1.Should().Be(entry2);
        entry1.Should().NotBe(entry3);
    }

    [Fact]
    public void OutboxMessageMetadata_Constructor_And_Properties_WorkCorrectly()
    {
        var entries = new[]
        {
            new MetadataEntry("TenantId", "tenant-1"),
            new MetadataEntry("UserId", "user-42")
        };

        var metadata = new OutboxMessageMetadata("corr-1", "caus-1", "MyEvent", entries);

        metadata.CorrelationId.Should().Be("corr-1");
        metadata.CausationId.Should().Be("caus-1");
        metadata.MessageType.Should().Be("MyEvent");
        metadata.Entries.Length.Should().Be(2);

        metadata.GetValue("TenantId").Should().Be("tenant-1");
        metadata.GetValue("UserId").Should().Be("user-42");
        metadata.GetValue("NonExistent").Should().BeNull();
        metadata.GetValue("tenantid").Should().BeNull(); // case sensitive
    }

    [Fact]
    public void OutboxMessageMetadata_GetValue_WhenEmptyEntries_ReturnsNull()
    {
        var metadata = new OutboxMessageMetadata("corr-1", "caus-1", "MyEvent");

        metadata.Entries.IsEmpty.Should().BeTrue();
        metadata.GetValue("AnyKey").Should().BeNull();

        var defaultMeta = default(OutboxMessageMetadata);
        defaultMeta.GetValue("AnyKey").Should().BeNull();
    }

    [Theory]
    [InlineData("ValidAlias")]
    [InlineData("orders.created.v1")]
    public void OutboxMessageAttribute_ValidAlias_SetsProperty(string alias)
    {
        var attr = new OutboxMessageAttribute(alias);
        attr.Alias.Should().Be(alias);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void OutboxMessageAttribute_InvalidAlias_ThrowsArgumentException(string? invalidAlias)
    {
        Action act = () => _ = new OutboxMessageAttribute(invalidAlias!);
        act.Should().Throw<ArgumentException>()
            .WithParameterName("alias")
            .WithMessage("*The alias cannot be null or empty*");
    }

    [Fact]
    public void InboxConsumerAttribute_ValidEventAlias_SetsProperties()
    {
        var attr1 = new InboxConsumerAttribute("order.placed");
        attr1.EventAlias.Should().Be("order.placed");
        attr1.MaxAgeMinutes.Should().Be(0);

        var attr2 = new InboxConsumerAttribute("order.placed", 60);
        attr2.EventAlias.Should().Be("order.placed");
        attr2.MaxAgeMinutes.Should().Be(60);

        attr2.MaxAgeMinutes = 120;
        attr2.MaxAgeMinutes.Should().Be(120);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void InboxConsumerAttribute_InvalidEventAlias_ThrowsArgumentException(string? invalidAlias)
    {
        Action act1 = () => _ = new InboxConsumerAttribute(invalidAlias!);
        act1.Should().Throw<ArgumentException>()
            .WithParameterName("eventAlias")
            .WithMessage("*The event alias cannot be null or empty*");

        Action act2 = () => _ = new InboxConsumerAttribute(invalidAlias!, 30);
        act2.Should().Throw<ArgumentException>()
            .WithParameterName("eventAlias")
            .WithMessage("*The event alias cannot be null or empty*");
    }

    [Fact]
    public void IdempotentConsumerAttribute_CanBeInstantiated()
    {
        var attr = new IdempotentConsumerAttribute();
        attr.Should().NotBeNull();
    }

    [Fact]
    public void IdempotencyRecord_PropertiesAndEquality_WorkCorrectly()
    {
        var now = DateTimeOffset.UtcNow;
        var record1 = new IdempotencyRecord("msg-1", "consumer-1", now);
        var record2 = new IdempotencyRecord("msg-1", "consumer-1", now);
        var record3 = new IdempotencyRecord("msg-2", "consumer-1", now);

        record1.MessageId.Should().Be("msg-1");
        record1.ConsumerId.Should().Be("consumer-1");
        record1.ProcessedAt.Should().Be(now);

        record1.Should().Be(record2);
        record1.Should().NotBe(record3);
    }

    private sealed class CustomTransactionContext : IOutboxTransactionContext
    {
        public object Transaction { get; set; } = "custom-tx";
        public object? Connection { get; set; } = "custom-conn";
    }

    [Fact]
    public void IOutboxTransactionContext_DefaultGetContext_ReturnsCastedContextOrNull()
    {
        IOutboxTransactionContext context = new CustomTransactionContext();

        context.GetContext<string>().Should().Be("custom-tx");
        context.GetContext<System.Data.Common.DbTransaction>().Should().BeNull();
    }
}
