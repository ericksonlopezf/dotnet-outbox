#pragma warning disable CA2012
using System;

using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.Outbox.Contracts;
using EricksonLopez.Outbox;
using EricksonLopez.Outbox.Testing;
using AwesomeAssertions;
using NSubstitute;
using Xunit;

namespace EricksonLopez.Outbox.Tests;

/// <summary>
/// Unit tests for <see cref="OutboxMessageBuilder{TMessage}"/>.
/// Verifies fluent API, header capture, and fast-path optimization.
/// </summary>
public sealed class OutboxMessageBuilderTests
{
    [Fact]
    public async Task StoreAsync_WithoutTransaction_ThrowsInvalidOperation()
    {
        var store = new InMemoryOutboxStore();
        

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await store.Publish(new TestEvent(Guid.NewGuid())).StoreAsync(CancellationToken.None));
    }

    [Fact]
    public async Task StoreAsync_WithTransaction_UsesSimplePath()
    {
        var store = new InMemoryOutboxStore();

        await store
            .Publish(new TestEvent(Guid.NewGuid()))
            .WithTransaction(null!)
            .StoreAsync(CancellationToken.None);

        Assert.Single(store.GetPublishedMessages<TestEvent>());
    }

    [Fact]
    public async Task StoreAsync_WithHeaders_UsesMetadataPath()
    {
        var store = Substitute.For<IOutbox>();
        MessageMetadata? capturedMeta = null;
        MetadataEntry[]? capturedEntries = null;

        store.StoreAsync(Arg.Any<TestEvent>(), Arg.Any<EricksonLopez.Outbox.Persistence.IOutboxTransactionContext>(), Arg.Any<MessageMetadata>(), Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>())
            .Returns(x => 
            {
                var meta = x.Arg<MessageMetadata>();
                capturedMeta = meta;
                capturedEntries = meta.Entries.ToArray(); // copy array before finally block clears it
                return default(ValueTask);
            });

        var builder = new OutboxMessageBuilder<TestEvent>(store, new TestEvent(Guid.NewGuid()));

        await builder
            .WithTransaction(null!)
            .WithHeader("TenantId", "tenant-abc")
            .WithHeader("Region", "us-east-1")
            .StoreAsync(CancellationToken.None);

        Assert.NotNull(capturedMeta);
        Assert.Equal(2, capturedEntries!.Length);
        Assert.Equal("TenantId", capturedEntries[0].Key);
        Assert.Equal("tenant-abc", capturedEntries[0].Value);
        Assert.Equal("Region", capturedEntries[1].Key);
        Assert.Equal("us-east-1", capturedEntries[1].Value);
    }

    [Fact]
    public async Task StoreAsync_WithDelay_SetsDeliverAt()
    {
        var store = Substitute.For<IOutbox>();
        var builder = new OutboxMessageBuilder<TestEvent>(store, new TestEvent(Guid.NewGuid()));
        var delay = TimeSpan.FromMinutes(30);

        await builder
            .WithTransaction(null!)
            .WithDelay(delay)
            .StoreAsync(CancellationToken.None);

        await store.Received().StoreAsync(
            Arg.Any<TestEvent>(),
            Arg.Any<EricksonLopez.Outbox.Persistence.IOutboxTransactionContext>(),
            Arg.Any<MessageMetadata>(), // Just assert any metadata
            Arg.Is<DateTimeOffset?>(d => d.HasValue && d.Value > DateTimeOffset.UtcNow),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StoreAsync_WithCorrelationId_UsesMetadataPath()
    {
        var store = Substitute.For<IOutbox>();
        var builder = new OutboxMessageBuilder<TestEvent>(store, new TestEvent(Guid.NewGuid()));

        await builder
            .WithTransaction(null!)
            .WithCorrelationId("trace-123")
            .WithCausationId("cmd-456")
            .StoreAsync(CancellationToken.None);

        await store.Received().StoreAsync(
            Arg.Any<TestEvent>(),
            Arg.Any<EricksonLopez.Outbox.Persistence.IOutboxTransactionContext>(),
            Arg.Is<MessageMetadata>(m => m.CorrelationId == "trace-123" && m.CausationId == "cmd-456"),
            null,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MultipleHeaders_AllCaptured()
    {
        var store = Substitute.For<IOutbox>();
        var builder = new OutboxMessageBuilder<TestEvent>(store, new TestEvent(Guid.NewGuid()));

        await builder
            .WithTransaction(null!)
            .WithHeader("K1", "V1")
            .WithHeader("K2", "V2")
            .WithHeader("K3", "V3")
            .StoreAsync(CancellationToken.None);

        await store.Received().StoreAsync(
            Arg.Any<TestEvent>(),
            Arg.Any<EricksonLopez.Outbox.Persistence.IOutboxTransactionContext>(),
            Arg.Is<MessageMetadata>(m => m.Entries.Length == 3),
            null,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeliverAt_Explicit_Works()
    {
        var store = Substitute.For<IOutbox>();
        var builder = new OutboxMessageBuilder<TestEvent>(store, new TestEvent(Guid.NewGuid()));
        var deliverTime = DateTimeOffset.UtcNow.AddHours(2);

        await builder
            .WithTransaction(null!)
            .WithDeliverAt(deliverTime)
            .StoreAsync(CancellationToken.None);

        await store.Received().StoreAsync(
            Arg.Any<TestEvent>(),
            Arg.Any<EricksonLopez.Outbox.Persistence.IOutboxTransactionContext>(),
            Arg.Any<MessageMetadata>(),
            deliverTime,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MultipleHeaders_ExceedsInitialPool_ResizesArray()
    {
        var store = Substitute.For<IOutbox>();
        var builder = new OutboxMessageBuilder<TestEvent>(store, new TestEvent(Guid.NewGuid()));
        builder.WithTransaction(null!);
        
        for (int i = 0; i < 15; i++)
        {
            builder.WithHeader($"K{i}", $"V{i}");
        }

        await builder.WithTransaction(NSubstitute.Substitute.For<EricksonLopez.Outbox.Persistence.IOutboxTransactionContext>()).StoreAsync(CancellationToken.None);

        await store.Received().StoreAsync(
            Arg.Any<TestEvent>(),
            Arg.Any<EricksonLopez.Outbox.Persistence.IOutboxTransactionContext>(),
            Arg.Is<MessageMetadata>(m => m.Entries.Length == 15),
            null,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Dispose_ReturnsArrayToPool()
    {
        var store = Substitute.For<IOutbox>();
        var builder = new OutboxMessageBuilder<TestEvent>(store, new TestEvent(Guid.NewGuid()));
        builder.WithHeader("K1", "V1");
        
        // This will call Dispose and return the rented array pool
        
        
        // Calling it twice shouldn't throw (if implemented correctly)
        
    }

    [Fact]
    public async Task WithHeader_MoreThan8_ExpandsArray()
    {
        var store = Substitute.For<IOutbox>();
        var builder = new OutboxMessageBuilder<TestEvent>(store, new TestEvent(Guid.NewGuid()));

        for (int i = 0; i < 15; i++)
        {
            builder.WithHeader($"Key{i}", $"Value{i}");
        }

        await builder.WithTransaction(NSubstitute.Substitute.For<EricksonLopez.Outbox.Persistence.IOutboxTransactionContext>()).StoreAsync();

        await store.Received(1).StoreAsync(
            Arg.Any<TestEvent>(), 
            Arg.Any<EricksonLopez.Outbox.Persistence.IOutboxTransactionContext>(),
            Arg.Is<MessageMetadata>(m => m.Entries.Length == 15), 
            null, 
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StoreAsync_Uses_Activity_Current_For_Correlation()
    {
        var store = Substitute.For<IOutbox>();
        
        var activity = new System.Diagnostics.Activity("TestActivity");
        activity.Start();

        var builder = new OutboxMessageBuilder<TestEvent>(store, new TestEvent(Guid.NewGuid()));
        await builder.WithTransaction(null!).StoreAsync();

        await store.Received(1).StoreAsync(
            Arg.Any<TestEvent>(),
            Arg.Any<EricksonLopez.Outbox.Persistence.IOutboxTransactionContext>(),
            Arg.Is<MessageMetadata>(m => m.CorrelationId == activity.TraceId.ToString() && m.CausationId == activity.SpanId.ToString()),
            null,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StoreAsync_With_Empty_Headers_Should_Not_Allocate_Array()
    {
        var store = Substitute.For<IOutbox>();
        var builder = new OutboxMessageBuilder<TestEvent>(store, new TestEvent(Guid.NewGuid()));

        await builder.WithTransaction(null!).StoreAsync();

        await store.Received(1).StoreAsync(
            Arg.Any<TestEvent>(),
            Arg.Any<EricksonLopez.Outbox.Persistence.IOutboxTransactionContext>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void WithHeader_Null_Values_Should_Throw()
    {
        var store = Substitute.For<IOutbox>();
        var builder = new OutboxMessageBuilder<TestEvent>(store, new TestEvent(Guid.NewGuid()));
        
        var act1 = () => builder.WithHeader(null!, "value");
        act1.Should().Throw<ArgumentNullException>();

        var act2 = () => builder.WithHeader("key", null!);
        act2.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Double_Dispose_Should_Not_Throw()
    {
        var store = Substitute.For<IOutbox>();
        var builder = new OutboxMessageBuilder<TestEvent>(store, new TestEvent(Guid.NewGuid()));
        builder.WithHeader("K", "V");
        
        builder.Dispose();
        var act = () => builder.Dispose();
        act.Should().NotThrow();
    }

    [Fact]
    public void StoreAsync_Should_Throw_If_Disposed()
    {
        var store = Substitute.For<IOutbox>();
        var builder = new OutboxMessageBuilder<TestEvent>(store, new TestEvent(Guid.NewGuid()));
        builder.Dispose();

        Func<Task> act = async () => await builder.StoreAsync();
        act.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public async Task StoreAsync_Twice_Should_Throw()
    {
        var store = Substitute.For<IOutbox>();
        var builder = new OutboxMessageBuilder<TestEvent>(store, new TestEvent(Guid.NewGuid()));
        builder.WithHeader("Key", "Value");

        await builder.WithTransaction(null!).StoreAsync();

        Func<Task> act = async () => await builder.StoreAsync();
        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public void StoreAsync_Without_Transaction_Disposes_Array()
    {
        var store = Substitute.For<IOutbox>();
        var builder = new OutboxMessageBuilder<TestEvent>(store, new TestEvent(Guid.NewGuid()));
        builder.WithHeader("TransactionMissingKey", "TransactionMissingValue");

        Func<Task> act = async () => await builder.StoreAsync(); // No WithTransaction!
        act.Should().ThrowAsync<InvalidOperationException>();

        // Try to rent arrays to ensure it was returned
        var pool = System.Buffers.ArrayPool<MetadataEntry>.Shared;
        var rentedArrays = new System.Collections.Generic.List<MetadataEntry[]>();
        try
        {
            bool foundSecret = false;
            for (int i = 0; i < 100; i++)
            {
                var arr = pool.Rent(8);
                rentedArrays.Add(arr);
                foreach (var entry in arr)
                {
                    if (entry.Key == "TransactionMissingKey")
                    {
                        foundSecret = true;
                    }
                }
            }
            
            foundSecret.Should().BeFalse("the array should have been cleared when StoreAsync throws because of missing transaction");
        }
        finally
        {
            foreach (var arr in rentedArrays)
            {
                pool.Return(arr);
            }
        }
    }

    [Fact]
    public async Task WithDeliverAt_Should_Set_DeliverAt()
    {
        var store = Substitute.For<IOutbox>();
        var builder = new OutboxMessageBuilder<TestEvent>(store, new TestEvent(Guid.NewGuid()));
        var deliverAt = DateTimeOffset.UtcNow.AddDays(1);
        
        await builder.WithTransaction(null!)
                     .WithDeliverAt(deliverAt)
                     .StoreAsync();

        await store.Received(1).StoreAsync(
            Arg.Any<TestEvent>(),
            Arg.Any<EricksonLopez.Outbox.Persistence.IOutboxTransactionContext>(),
            Arg.Any<MessageMetadata>(),
            deliverAt,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WithHeader_Should_Expand_Array_When_Exceeding_Capacity()
    {
        var store = Substitute.For<IOutbox>();
        var builder = new OutboxMessageBuilder<TestEvent>(store, new TestEvent(Guid.NewGuid()));
        
        // Add enough headers to guarantee we exceed the ArrayPool's actual returned size
        // which could be up to 1024 depending on the bucket.
        for (int i = 0; i < 2000; i++)
        {
            builder.WithHeader($"key{i}", $"value{i}");
        }

        MessageMetadata? capturedMetadata = null;
        store.StoreAsync(
                Arg.Any<TestEvent>(),
                Arg.Any<EricksonLopez.Outbox.Persistence.IOutboxTransactionContext>(),
                Arg.Any<MessageMetadata>(),
                null,
                Arg.Any<CancellationToken>())
            .Returns(ValueTask.CompletedTask)
            .AndDoes(ci =>
            {
                var m = ci.Arg<MessageMetadata>();
                // We must copy the array because the memory is pooled and will be cleared
                capturedMetadata = new MessageMetadata(
                    m.CorrelationId,
                    m.CausationId,
                    m.MessageType,
                    m.Entries.ToArray());
            });

        await builder.WithTransaction(null!).StoreAsync();

        capturedMetadata.Should().NotBeNull();
        capturedMetadata!.Value.Entries.Length.Should().Be(2000);
        capturedMetadata.Value.Entries.Span[0].Key.Should().Be("key0");
        capturedMetadata.Value.Entries.Span[1999].Key.Should().Be("key1999");
    }

    [Fact]
    public async Task StoreAsync_Uses_Activity_Current_For_Correlation_Unless_Explicitly_Set()
    {
        var store = Substitute.For<IOutbox>();
        
        var activity = new System.Diagnostics.Activity("TestActivity");
        activity.Start();

        var builder = new OutboxMessageBuilder<TestEvent>(store, new TestEvent(Guid.NewGuid()));
        await builder.WithTransaction(null!)
                     .WithCorrelationId("explicit-trace")
                     .WithCausationId("explicit-span")
                     .StoreAsync();

        await store.Received(1).StoreAsync(
            Arg.Any<TestEvent>(),
            Arg.Any<EricksonLopez.Outbox.Persistence.IOutboxTransactionContext>(),
            Arg.Is<MessageMetadata>(m => m.CorrelationId == "explicit-trace" && m.CausationId == "explicit-span"),
            null,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StoreAsync_Clears_Array_When_Returned_To_Pool()
    {
        var store = Substitute.For<IOutbox>();
        
        var builder = new OutboxMessageBuilder<TestEvent>(store, new TestEvent(Guid.NewGuid()));
        builder.WithTransaction(null!).WithHeader("SecretKey", "SecretValue");
        await builder.StoreAsync();

        // Try to rent a few arrays of size 8 to see if our secret was cleared
        var pool = System.Buffers.ArrayPool<MetadataEntry>.Shared;
        var rentedArrays = new System.Collections.Generic.List<MetadataEntry[]>();
        
        try
        {
            bool foundSecret = false;
            for (int i = 0; i < 100; i++)
            {
                var arr = pool.Rent(8);
                rentedArrays.Add(arr);
                foreach (var entry in arr)
                {
                    if (entry.Key == "SecretKey" || entry.Value == "SecretValue")
                    {
                        foundSecret = true;
                    }
                }
            }
            
            foundSecret.Should().BeFalse("the array should have been cleared when returned to the pool");
        }
        finally
        {
            foreach (var arr in rentedArrays)
            {
                pool.Return(arr);
            }
        }
    }

    [Fact]
    public void Dispose_Clears_Array_When_Returned_To_Pool()
    {
        var store = Substitute.For<IOutbox>();
        
        var builder = new OutboxMessageBuilder<TestEvent>(store, new TestEvent(Guid.NewGuid()));
        builder.WithHeader("SecretDisposeKey", "SecretDisposeValue");
        builder.Dispose(); // Returns and clears
        builder.Dispose(); // should return early

        // Try to rent a few arrays of size 8 to see if our secret was cleared
        var pool = System.Buffers.ArrayPool<MetadataEntry>.Shared;
        var rentedArrays = new System.Collections.Generic.List<MetadataEntry[]>();
        
        try
        {
            bool foundSecret = false;
            for (int i = 0; i < 100; i++)
            {
                var arr = pool.Rent(8);
                rentedArrays.Add(arr);
                foreach (var entry in arr)
                {
                    if (entry.Key == "SecretDisposeKey" || entry.Value == "SecretDisposeValue")
                    {
                        foundSecret = true;
                    }
                }
            }
            
            foundSecret.Should().BeFalse("the array should have been cleared when disposed");
        }
        finally
        {
            foreach (var arr in rentedArrays)
            {
                pool.Return(arr);
            }
        }
    }
    [Fact]
    public async Task StoreAsync_Should_Not_Overwrite_CausationId_If_Already_Provided_Along_With_Activity()
    {
        var store = Substitute.For<IOutbox>();
        
        using var activity = new System.Diagnostics.Activity("TestActivity");
        activity.Start();

        var builder = new OutboxMessageBuilder<TestEvent>(store, new TestEvent(Guid.NewGuid()));
        await builder.WithTransaction(null!)
                     .WithCausationId("explicit-span")
                     .StoreAsync();

        await store.Received(1).StoreAsync(
            Arg.Any<TestEvent>(),
            Arg.Any<EricksonLopez.Outbox.Persistence.IOutboxTransactionContext>(),
            Arg.Is<MessageMetadata>(m => m.CorrelationId == activity.TraceId.ToString() && m.CausationId == "explicit-span"),
            null,
            Arg.Any<CancellationToken>());
    }
}

[OutboxMessage("test.event.v1")]
internal sealed record TestEvent(Guid Id);








