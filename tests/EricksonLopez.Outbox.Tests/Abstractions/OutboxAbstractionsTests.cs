// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using EricksonLopez.Outbox.Contracts;
using EricksonLopez.Outbox.Persistence;
using NSubstitute;
using Xunit;

namespace EricksonLopez.Outbox.Tests.Abstractions;

public class OutboxAbstractionsTests
{
    [Fact]
    public void MetadataEntry_Should_Hold_Key_And_Value_And_Support_Equality()
    {
        var entry1 = new MetadataEntry("CorrelationId", "12345");
        var entry2 = new MetadataEntry("CorrelationId", "12345");
        var entry3 = new MetadataEntry("CausationId", "67890");

        entry1.Key.Should().Be("CorrelationId");
        entry1.Value.Should().Be("12345");

        (entry1 == entry2).Should().BeTrue();
        (entry1 != entry3).Should().BeTrue();
        entry1.Equals(entry2).Should().BeTrue();
        entry1.GetHashCode().Should().Be(entry2.GetHashCode());
    }

    [Fact]
    public void MessageMetadata_Default_Constructor_Should_Have_Null_Properties_And_Empty_Entries()
    {
        var metadata = new OutboxMessageMetadata();

        metadata.CorrelationId.Should().BeNull();
        metadata.CausationId.Should().BeNull();
        metadata.MessageType.Should().BeNull();
        metadata.Entries.IsEmpty.Should().BeTrue();
        metadata.GetValue("any").Should().BeNull();
    }

    [Fact]
    public void MessageMetadata_Parameterized_Constructor_Should_Assign_All_Properties()
    {
        var entries = new MetadataEntry[]
        {
            new("TenantId", "tenant-1"),
            new("Environment", "Production"),
            new("tenantid", "lowercase-tenant")
        };

        var metadata = new OutboxMessageMetadata("corr-100", "caus-200", "OrderCreatedEvent", entries);

        metadata.CorrelationId.Should().Be("corr-100");
        metadata.CausationId.Should().Be("caus-200");
        metadata.MessageType.Should().Be("OrderCreatedEvent");
        metadata.Entries.Length.Should().Be(3);

        // GetValue checks with exact StringComparison.Ordinal
        metadata.GetValue("TenantId").Should().Be("tenant-1");
        metadata.GetValue("Environment").Should().Be("Production");
        metadata.GetValue("tenantid").Should().Be("lowercase-tenant");
        metadata.GetValue("NonExistent").Should().BeNull();
    }

    [Fact]
    public void MessageMetadata_GetValue_Ordinal_Case_Sensitivity_Should_Be_Enforced()
    {
        var entries = new MetadataEntry[]
        {
            new("X-Trace-Id", "trace-999")
        };

        var metadata = new OutboxMessageMetadata(null, null, null, entries);

        metadata.GetValue("X-Trace-Id").Should().Be("trace-999");
        metadata.GetValue("x-trace-id").Should().BeNull();
        metadata.GetValue("X-TRACE-ID").Should().BeNull();
    }

    [Fact]
    public void InboxConsumerAttribute_Two_Parameter_Constructor_Should_Set_Properties()
    {
        var attr = new InboxConsumerAttribute("order.created", 60);

        attr.EventAlias.Should().Be("order.created");
        attr.MaxAgeMinutes.Should().Be(60);

        attr.MaxAgeMinutes = 120;
        attr.MaxAgeMinutes.Should().Be(120);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void InboxConsumerAttribute_Two_Parameter_Constructor_Should_Throw_On_Invalid_Alias(string? invalidAlias)
    {
        var act = () => new InboxConsumerAttribute(invalidAlias!, 30);
        act.Should().Throw<ArgumentException>()
            .WithParameterName("eventAlias")
            .WithMessage("*The event alias cannot be null or empty.*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void OutboxMessageAttribute_Constructor_Should_Throw_On_Invalid_Alias(string? invalidAlias)
    {
        var act = () => new OutboxMessageAttribute(invalidAlias!);
        act.Should().Throw<ArgumentException>()
            .WithParameterName("alias")
            .WithMessage("*The alias cannot be null or empty.*");
    }

    [Fact]
    public void IOutboxTransactionContext_GetContext_Default_Implementation_Should_Cast_Correctly()
    {
        var txMock = Substitute.For<DbTransaction>();
        var connMock = Substitute.For<DbConnection>();

        IOutboxTransactionContext context = new TestTransactionContext(txMock, connMock);

        context.Transaction.Should().BeSameAs(txMock);
        context.Connection.Should().BeSameAs(connMock);

        // GetContext should return cast instance when matching
        context.GetContext<DbTransaction>().Should().BeSameAs(txMock);

        // GetContext should return null when cast fails
        context.GetContext<string>().Should().BeNull();
    }

    [Fact]
    public async Task OutboxExtensions_StoreAsync_IEnumerable_Should_Throw_When_Outbox_Is_Null()
    {
        IOutbox outbox = null!;
        var tx = Substitute.For<IOutboxTransactionContext>();
        var messages = new List<string> { "msg1" };

        var act = async () => await OutboxExtensions.StoreAsync(outbox, messages, tx);
        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("outbox");
    }

    [Fact]
    public async Task OutboxExtensions_StoreAsync_IEnumerable_Should_Throw_When_Messages_Is_Null()
    {
        var outbox = new TestOutbox();
        var tx = Substitute.For<IOutboxTransactionContext>();
        IEnumerable<string> messages = null!;

        var act = async () => await OutboxExtensions.StoreAsync(outbox, messages, tx);
        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("messages");
    }

    [Fact]
    public async Task OutboxExtensions_StoreAsync_Empty_Collection_Should_Return_Completed_Task_Without_Calling_Outbox()
    {
        var outbox = new TestOutbox();
        var tx = Substitute.For<IOutboxTransactionContext>();

        var emptyList = new List<string>();
        await OutboxExtensions.StoreAsync(outbox, emptyList, tx);

        outbox.BatchCallCount.Should().Be(0);
    }

    [Fact]
    public async Task OutboxExtensions_StoreAsync_Empty_NonCollection_Sequence_Should_Return_Completed_Task()
    {
        var outbox = new TestOutbox();
        var tx = Substitute.For<IOutboxTransactionContext>();

        static IEnumerable<string> GetEmptyYield()
        {
            yield break;
        }

        await OutboxExtensions.StoreAsync(outbox, GetEmptyYield(), tx);

        outbox.BatchCallCount.Should().Be(0);
    }

    [Fact]
    public async Task OutboxExtensions_StoreAsync_ICollection_Branch_Should_Convert_And_Delegate_To_Outbox()
    {
        var outbox = new TestOutbox();
        var tx = Substitute.For<IOutboxTransactionContext>();
        using var cts = new CancellationTokenSource();

        var messages = new List<string> { "item1", "item2", "item3" };

        await OutboxExtensions.StoreAsync(outbox, messages, tx, cts.Token);

        outbox.BatchCallCount.Should().Be(1);
        outbox.LastBatchTransaction.Should().BeSameAs(tx);
        outbox.LastBatchCancellationToken.Should().Be(cts.Token);
        var memory = (ReadOnlyMemory<string>)outbox.LastBatchMemory!;
        memory.Length.Should().Be(3);
        memory.Span[0].Should().Be("item1");
        memory.Span[1].Should().Be("item2");
        memory.Span[2].Should().Be("item3");
    }

    [Fact]
    public async Task OutboxExtensions_StoreAsync_Non_ICollection_Enumerable_Branch_Should_Convert_And_Delegate_To_Outbox()
    {
        var outbox = new TestOutbox();
        var tx = Substitute.For<IOutboxTransactionContext>();
        using var cts = new CancellationTokenSource();

        static IEnumerable<int> GenerateNumbers()
        {
            yield return 10;
            yield return 20;
            yield return 30;
        }

        await OutboxExtensions.StoreAsync(outbox, GenerateNumbers(), tx, cts.Token);

        outbox.BatchCallCount.Should().Be(1);
        outbox.LastBatchTransaction.Should().BeSameAs(tx);
        outbox.LastBatchCancellationToken.Should().Be(cts.Token);
        var memory = (ReadOnlyMemory<int>)outbox.LastBatchMemory!;
        memory.Length.Should().Be(3);
        memory.Span[0].Should().Be(10);
        memory.Span[1].Should().Be(20);
        memory.Span[2].Should().Be(30);
    }

    [Fact]
    public void Generic_IOutboxTransactionContext_Should_Implement_Strongly_Typed_Properties()
    {
        var conn = Substitute.For<DbConnection>();
        var tx = Substitute.For<DbTransaction>();

        var genericContext = new TestGenericTransactionContext<DbConnection, DbTransaction>(conn, tx);

        genericContext.Connection.Should().BeSameAs(conn);
        genericContext.Transaction.Should().BeSameAs(tx);

        ((IOutboxTransactionContext)genericContext).Connection.Should().BeSameAs(conn);
        ((IOutboxTransactionContext)genericContext).Transaction.Should().BeSameAs(tx);
    }

    private sealed class TestOutbox : IOutbox
    {
        public int BatchCallCount { get; private set; }
        public object? LastBatchMemory { get; private set; }
        public IOutboxTransactionContext? LastBatchTransaction { get; private set; }
        public CancellationToken LastBatchCancellationToken { get; private set; }

        public ValueTask StoreAsync<TMessage>(TMessage message, IOutboxTransactionContext transaction, CancellationToken cancellationToken = default) where TMessage : notnull
        {
            return ValueTask.CompletedTask;
        }

        public ValueTask StoreAsync<TMessage>(ReadOnlyMemory<TMessage> messages, IOutboxTransactionContext transaction, CancellationToken cancellationToken = default) where TMessage : notnull
        {
            BatchCallCount++;
            LastBatchMemory = messages;
            LastBatchTransaction = transaction;
            LastBatchCancellationToken = cancellationToken;
            return ValueTask.CompletedTask;
        }

        public ValueTask StoreAsync<TMessage>(TMessage message, IOutboxTransactionContext transaction, OutboxMessageMetadata metadata, DateTimeOffset? deliverAt, CancellationToken cancellationToken = default) where TMessage : notnull
        {
            return ValueTask.CompletedTask;
        }
    }

    private sealed class TestTransactionContext : IOutboxTransactionContext
    {
        public object Transaction { get; }
        public object? Connection { get; }

        public TestTransactionContext(object transaction, object? connection)
        {
            Transaction = transaction;
            Connection = connection;
        }
    }

    private sealed class TestGenericTransactionContext<TConn, TTx> : IOutboxTransactionContext<TConn, TTx>
    {
        public TConn? Connection { get; }
        public TTx? Transaction { get; }

        object? IOutboxTransactionContext.Connection => Connection;
        object IOutboxTransactionContext.Transaction => Transaction!;

        public TestGenericTransactionContext(TConn? connection, TTx? transaction)
        {
            Connection = connection;
            Transaction = transaction;
        }
    }
}

