// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using EricksonLopez.Outbox;
using EricksonLopez.Outbox.Contracts;
using EricksonLopez.Outbox.Testing;
using NSubstitute;
using Xunit;

#pragma warning disable CA2012
namespace EricksonLopez.Outbox.Tests.Delivery;

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

        var action = async () => await store.Publish(new TestEvent(Guid.NewGuid())).StoreAsync(CancellationToken.None);

        var ex = await action.Should().ThrowAsync<InvalidOperationException>();
        ex.WithMessage("A transaction must be provided via WithTransaction() before calling StoreAsync().");
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
        OutboxMessageMetadata? capturedMeta = null;
        MetadataEntry[]? capturedEntries = null;

        store.StoreAsync(Arg.Any<TestEvent>(), Arg.Any<EricksonLopez.Outbox.Persistence.IOutboxTransactionContext>(), Arg.Any<OutboxMessageMetadata>(), Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>())
            .Returns(x => 
            {
                var meta = x.Arg<OutboxMessageMetadata>();
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

        var act = async () => await builder.StoreAsync();
        await act.Should().ThrowAsync<ObjectDisposedException>();
        builder.Dispose();
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
            Arg.Any<OutboxMessageMetadata>(), // Just assert any metadata
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
            Arg.Is<OutboxMessageMetadata>(m => m.CorrelationId == "trace-123" && m.CausationId == "cmd-456"),
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
            Arg.Is<OutboxMessageMetadata>(m => m.Entries.Length == 3),
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
            Arg.Any<OutboxMessageMetadata>(),
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
            Arg.Is<OutboxMessageMetadata>(m => m.Entries.Length == 15),
            null,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dispose_ReturnsArrayToPool()
    {
        var store = Substitute.For<IOutbox>();
        var builder = new OutboxMessageBuilder<TestEvent>(store, new TestEvent(Guid.NewGuid()));
        builder.WithTransaction(Substitute.For<EricksonLopez.Outbox.Persistence.IOutboxTransactionContext>());
        builder.WithHeader("K1", "V1");
        
        builder.Dispose();
        
        var act = async () => await builder.StoreAsync();
        await act.Should().ThrowAsync<ObjectDisposedException>();

        // Calling it twice shouldn't throw
        builder.Dispose();
    }

    [Fact]
    public async Task Dispose_WithoutHeaders_MarksDisposed()
    {
        var store = Substitute.For<IOutbox>();
        var builder = new OutboxMessageBuilder<TestEvent>(store, new TestEvent(Guid.NewGuid()));
        
        builder.Dispose();
        
        var act = async () => await builder.StoreAsync();
        await act.Should().ThrowAsync<ObjectDisposedException>();
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
            Arg.Is<OutboxMessageMetadata>(m => m.Entries.Length == 15), 
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
            Arg.Is<OutboxMessageMetadata>(m => m.CorrelationId == activity.TraceId.ToString() && m.CausationId == activity.SpanId.ToString()),
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
    public async Task StoreAsync_Without_Transaction_Disposes_Array()
    {
        var store = Substitute.For<IOutbox>();
        var builder = new OutboxMessageBuilder<TestEvent>(store, new TestEvent(Guid.NewGuid()));
        builder.WithHeader("TransactionMissingKey", "TransactionMissingValue");

        Func<Task> act = async () => await builder.StoreAsync(); // No WithTransaction!
        await act.Should().ThrowAsync<InvalidOperationException>();

        // Must be marked disposed after the missing transaction failure
        var act2 = async () => await builder.StoreAsync();
        await act2.Should().ThrowAsync<ObjectDisposedException>();

        // Try to rent arrays to ensure it was returned
        var pool = System.Buffers.ArrayPool<MetadataEntry>.Shared;
        var rentedArrays = new List<MetadataEntry[]>();
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
            Arg.Any<OutboxMessageMetadata>(),
            deliverAt,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WithHeader_Should_Expand_Array_When_Exceeding_Capacity()
    {
        var store = Substitute.For<IOutbox>();
        var builder = new OutboxMessageBuilder<TestEvent>(store, new TestEvent(Guid.NewGuid()));
        
        // Add enough headers to guarantee we exceed the ArrayPool's actual rented size (16 -> 32)
        for (int i = 0; i < 17; i++)
        {
            builder.WithHeader($"key{i}", $"value{i}");
        }

        OutboxMessageMetadata? capturedMetadata = null;
        store.StoreAsync(
                Arg.Any<TestEvent>(),
                Arg.Any<EricksonLopez.Outbox.Persistence.IOutboxTransactionContext>(),
                Arg.Any<OutboxMessageMetadata>(),
                null,
                Arg.Any<CancellationToken>())
            .Returns(ValueTask.CompletedTask)
            .AndDoes(ci =>
            {
                var m = ci.Arg<OutboxMessageMetadata>();
                // We must copy the array because the memory is pooled and will be cleared
                capturedMetadata = new OutboxMessageMetadata(
                    m.CorrelationId,
                    m.CausationId,
                    m.MessageType,
                    m.Entries.ToArray());
            });

        await builder.WithTransaction(null!).StoreAsync();

        capturedMetadata.Should().NotBeNull();
        capturedMetadata!.Value.Entries.Length.Should().Be(17);
        capturedMetadata.Value.Entries.Span[0].Key.Should().Be("key0");
        capturedMetadata.Value.Entries.Span[16].Key.Should().Be("key16");
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
            Arg.Is<OutboxMessageMetadata>(m => m.CorrelationId == "explicit-trace" && m.CausationId == "explicit-span"),
            null,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StoreAsync_Clears_Array_When_Returned_To_Pool()
    {
        var store = Substitute.For<IOutbox>();
        MetadataEntry[]? capturedArray = null;

        store.StoreAsync(
            Arg.Any<TestEvent>(),
            Arg.Any<EricksonLopez.Outbox.Persistence.IOutboxTransactionContext>(),
            Arg.Do<OutboxMessageMetadata>(m =>
            {
                if (System.Runtime.InteropServices.MemoryMarshal.TryGetArray(m.Entries, out ArraySegment<MetadataEntry> segment))
                {
                    capturedArray = segment.Array;
                }
            }),
            Arg.Any<DateTimeOffset?>(),
            Arg.Any<CancellationToken>())
            .Returns(ValueTask.CompletedTask);

        var builder = new OutboxMessageBuilder<TestEvent>(store, new TestEvent(Guid.NewGuid()));
        builder.WithTransaction(Substitute.For<EricksonLopez.Outbox.Persistence.IOutboxTransactionContext>())
               .WithHeader("SecretKey", "SecretValue");
        
        await builder.StoreAsync();

        capturedArray.Should().NotBeNull();
        // Since ArrayPool.Return(..., clearArray: true) was executed by Dispose() inside finally block,
        // the underlying array elements MUST have been cleared.
        capturedArray![0].Key.Should().BeNull();
        capturedArray[0].Value.Should().BeNull();
    }

    [Fact]
    public void Dispose_Clears_Array_When_Returned_To_Pool()
    {
        var pool = System.Buffers.ArrayPool<MetadataEntry>.Shared;
        
        // Prime the thread-local pool bucket
        var primed = pool.Rent(8);
        pool.Return(primed, clearArray: true);

        var store = Substitute.For<IOutbox>();
        var builder = new OutboxMessageBuilder<TestEvent>(store, new TestEvent(Guid.NewGuid()));
        builder.WithHeader("SecretDisposeKey", "SecretDisposeValue");
        
        builder.Dispose(); // Returns and clears

        // Rent from the same thread's pool bucket
        var rentedBack = pool.Rent(8);
        try
        {
            rentedBack.Should().BeSameAs(primed);
            rentedBack[0].Key.Should().BeNull();
            rentedBack[0].Value.Should().BeNull();
        }
        finally
        {
            pool.Return(rentedBack, clearArray: true);
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
            Arg.Is<OutboxMessageMetadata>(m => m.CorrelationId == activity.TraceId.ToString() && m.CausationId == "explicit-span"),
            null,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WithHeader_Exact8And9Headers_ExpandsArrayAt9()
    {
        var store = Substitute.For<IOutbox>();
        var builder = new OutboxMessageBuilder<TestEvent>(store, new TestEvent(Guid.NewGuid()));
        
        for (int i = 0; i < 9; i++)
        {
            builder.WithHeader($"K{i}", $"V{i}");
        }

        await builder.WithTransaction(Substitute.For<EricksonLopez.Outbox.Persistence.IOutboxTransactionContext>()).StoreAsync();

        await store.Received(1).StoreAsync(
            Arg.Any<TestEvent>(),
            Arg.Any<EricksonLopez.Outbox.Persistence.IOutboxTransactionContext>(),
            Arg.Is<OutboxMessageMetadata>(m => m.Entries.Length == 9),
            null,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StoreAsync_WithCorrelationOnly_PassesDefaultEntriesMemory()
    {
        var store = Substitute.For<IOutbox>();
        var builder = new OutboxMessageBuilder<TestEvent>(store, new TestEvent(Guid.NewGuid()));
        
        await builder.WithTransaction(Substitute.For<EricksonLopez.Outbox.Persistence.IOutboxTransactionContext>())
                     .WithCorrelationId("corr-only")
                     .StoreAsync();

        await store.Received(1).StoreAsync(
            Arg.Any<TestEvent>(),
            Arg.Any<EricksonLopez.Outbox.Persistence.IOutboxTransactionContext>(),
            Arg.Is<OutboxMessageMetadata>(m => m.CorrelationId == "corr-only" && m.Entries.IsEmpty),
            null,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WithTenantId_Should_Add_Tenant_Header()
    {
        var store = Substitute.For<IOutbox>();
        var builder = new OutboxMessageBuilder<TestEvent>(store, new TestEvent(Guid.NewGuid()));
        OutboxMessageMetadata captured = default;

        store.StoreAsync(
            Arg.Any<TestEvent>(),
            Arg.Any<EricksonLopez.Outbox.Persistence.IOutboxTransactionContext>(),
            Arg.Do<OutboxMessageMetadata>(m => captured = new OutboxMessageMetadata(m.CorrelationId, m.CausationId, m.MessageType, m.Entries.ToArray())),
            Arg.Any<DateTimeOffset?>(),
            Arg.Any<CancellationToken>())
            .Returns(ValueTask.CompletedTask);

        await builder.WithTransaction(Substitute.For<EricksonLopez.Outbox.Persistence.IOutboxTransactionContext>())
                     .WithTenantId("tenant-42")
                     .StoreAsync();

        captured.Entries.Length.Should().Be(1);
        captured.Entries.Span[0].Key.Should().Be("x-tenant-id");
        captured.Entries.Span[0].Value.Should().Be("tenant-42");
    }

    [Fact]
    public void WithTenantId_Null_Should_Throw()
    {
        var store = Substitute.For<IOutbox>();
        var builder = new OutboxMessageBuilder<TestEvent>(store, new TestEvent(Guid.NewGuid()));

        var act = () => builder.WithTenantId(null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("tenantId");
    }

    [Fact]
    public async Task MultipleHeaders_50_Headers_Resizes_Correctly_And_Preserves_All_Entries()
    {
        var store = Substitute.For<IOutbox>();
        var builder = new OutboxMessageBuilder<TestEvent>(store, new TestEvent(Guid.NewGuid()));
        OutboxMessageMetadata captured = default;

        store.StoreAsync(
            Arg.Any<TestEvent>(),
            Arg.Any<EricksonLopez.Outbox.Persistence.IOutboxTransactionContext>(),
            Arg.Do<OutboxMessageMetadata>(m => captured = new OutboxMessageMetadata(m.CorrelationId, m.CausationId, m.MessageType, m.Entries.ToArray())),
            Arg.Any<DateTimeOffset?>(),
            Arg.Any<CancellationToken>())
            .Returns(ValueTask.CompletedTask);

        for (int i = 0; i < 50; i++)
        {
            builder.WithHeader($"k{i}", $"v{i}");
        }

        await builder.WithTransaction(Substitute.For<EricksonLopez.Outbox.Persistence.IOutboxTransactionContext>()).StoreAsync();

        captured.Entries.Length.Should().Be(50);
        for (int i = 0; i < 50; i++)
        {
            captured.Entries.Span[i].Key.Should().Be($"k{i}");
            captured.Entries.Span[i].Value.Should().Be($"v{i}");
        }
    }

    [Fact]
    public void Dispose_With_Headers_Sets_Disposed_And_Returns_Array()
    {
        var store = Substitute.For<IOutbox>();
        var builder = new OutboxMessageBuilder<TestEvent>(store, new TestEvent(Guid.NewGuid()));
        builder.WithHeader("KeyA", "ValA");

        builder.Dispose();

        // Calling Dispose a second time should not throw
        builder.Dispose();

        Func<Task> act = async () => await builder.StoreAsync();
        act.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public void Resize_Returns_And_Clears_Previous_Array_To_Pool()
    {
        var pool = System.Buffers.ArrayPool<MetadataEntry>.Shared;

        // Prime the size-8 thread-local pool bucket
        var primed = pool.Rent(8);
        pool.Return(primed, clearArray: true);

        var store = Substitute.For<IOutbox>();
        var builder = new OutboxMessageBuilder<TestEvent>(store, new TestEvent(Guid.NewGuid()));

        // Rent primed buffer (size 16 in standard ArrayPool implementation)
        builder.WithHeader("FirstKey", "FirstValue");

        // Add enough headers to force resizing beyond the initial buffer
        for (int i = 1; i < 20; i++)
        {
            builder.WithHeader($"k{i}", $"v{i}");
        }

        // The initial primed array MUST have been returned to the pool with clearArray: true
        var rentedBack = pool.Rent(8);
        try
        {
            rentedBack.Should().BeSameAs(primed);
            rentedBack[0].Key.Should().BeNull();
            rentedBack[0].Value.Should().BeNull();
        }
        finally
        {
            pool.Return(rentedBack, clearArray: true);
            builder.Dispose();
        }
    }
}

[OutboxMessage("test.event.v1")]
internal sealed record TestEvent(Guid Id);













