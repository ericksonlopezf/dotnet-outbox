#pragma warning disable CA2012
using System;
using System.Buffers;
using System.Collections.Generic;

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;

using EricksonLopez.Outbox.Contracts;
using EricksonLopez.Outbox;
using EricksonLopez.Outbox.Persistence;
using EricksonLopez.Outbox.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace EricksonLopez.Outbox.Tests;

public record TestMessage1;

public class DefaultOutboxTests
{
    private readonly IOutboxRepository _repo;
    private readonly IOutboxSerializer _serializer;
    private readonly IOutboxMessageTypeResolver _resolver;
    private readonly DefaultOutbox _outbox;
    private readonly EricksonLopez.Outbox.Persistence.IOutboxTransactionContext _transaction;

    public DefaultOutboxTests()
    {
        _repo = Substitute.For<IOutboxRepository>();
        // FakeSerializer: NSubstitute cannot intercept generic default interface methods (Serialize<T>).
        // A concrete fake is the correct solution for testing IOutboxSerializer hot paths.
        _serializer = new FakeSerializer();
        _resolver = Substitute.For<IOutboxMessageTypeResolver>();
        _transaction = Substitute.For<EricksonLopez.Outbox.Persistence.IOutboxTransactionContext>();

        _resolver.GetAlias(Arg.Any<Type>()).Returns("TestAlias");

        var options = Microsoft.Extensions.Options.Options.Create(new EricksonLopez.Outbox.OutboxRuntimeOptions());
        _outbox = new DefaultOutbox(_repo, _serializer, _resolver, options, new EricksonLopez.Outbox.Diagnostics.OutboxMetrics());
    }

    [Fact]
    public async Task StoreAsync_Should_Build_And_Insert_Single_Message()
    {
        var msg = new TestMessage { Data = "Hello" };

        await _outbox.StoreAsync(msg, _transaction);

        await _repo.Received(1).InsertAsync(
            Arg.Is<OutboxMessage>(m => 
                m.MessageType == "TestAlias" && 
                m.Payload.Length == 3 &&
                m.Headers.Length == 2 &&
                m.Status == 0),
            _transaction,
            Arg.Any<CancellationToken>());
            
        // Test ArgumentNullException
        var ex = await Assert.ThrowsAsync<ArgumentNullException>(() => _outbox.StoreAsync(msg, null!).AsTask());
        ex.ParamName.Should().Be("transaction");
    }

    [Fact]
    public async Task StoreAsync_With_AsyncRepository_Should_Await_And_Record_Metrics()
    {
        var msg = new TestMessage { Data = "Hello" };
        var repo = Substitute.For<IOutboxRepository>();
        var options = Microsoft.Extensions.Options.Options.Create(new EricksonLopez.Outbox.OutboxRuntimeOptions());
        var outbox = new DefaultOutbox(repo, _serializer, _resolver, options, new EricksonLopez.Outbox.Diagnostics.OutboxMetrics());

        repo.InsertAsync(Arg.Any<OutboxMessage>(), Arg.Any<IOutboxTransactionContext>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask(Task.Delay(1)));

        await outbox.StoreAsync(msg, _transaction);

        await repo.Received(1).InsertAsync(Arg.Any<OutboxMessage>(), _transaction, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StoreAsync_Should_Build_And_Insert_Multiple_Messages()
    {
        var msgs = new[] { new TestMessage { Data = "A" }, new TestMessage { Data = "B" } };

        await _outbox.StoreAsync<TestMessage>(msgs, _transaction);

        await _repo.Received(1).InsertBatchAsync(
            Arg.Is<ReadOnlyMemory<OutboxMessage>>(msgs => msgs.Length == 2),
            _transaction,
            Arg.Any<CancellationToken>());

        // Test ArgumentNullException
        var ex = await Assert.ThrowsAsync<ArgumentNullException>(() => _outbox.StoreAsync<TestMessage>(msgs, null!).AsTask());
        ex.ParamName.Should().Be("transaction");
    }

    [Fact]
    public async Task Publish_Should_Return_Builder()
    {
        var builder = _outbox.Publish(new TestMessage { Data = "Hello" });
        await builder.WithTransaction(_transaction).StoreAsync();
        
        await _repo.Received(1).InsertAsync(Arg.Any<OutboxMessage>(), _transaction, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task IntegrationEvent_Should_Use_EventId()
    {
        var eventId = Guid.NewGuid();
        var ev = new TestIntegrationEvent { EventId = eventId };
        
        await _outbox.StoreAsync(ev, _transaction);
        
        await _repo.Received(1).InsertAsync(
            Arg.Is<OutboxMessage>(m => m.Id == eventId),
            _transaction,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StoreAsync_With_Metadata_Should_Map_Properly()
    {
        var metadata = new MessageMetadata("corr-1", "caus-1", "CustomType", new[] { new MetadataEntry("key", "val") });
        
        await _outbox.StoreAsync(new TestMessage(), _transaction, metadata, DateTimeOffset.UtcNow);
        
        await _repo.Received(1).InsertAsync(
            Arg.Is<OutboxMessage>(m => 
                m.MessageType == "CustomType" && 
                m.CorrelationId == "corr-1" && 
                m.CausationId == "caus-1" &&
                m.DeliverAt.HasValue &&
                System.Text.Encoding.UTF8.GetString(m.Headers.ToArray()).Contains("key") && System.Text.Encoding.UTF8.GetString(m.Headers.ToArray()).Contains("val")),
            _transaction,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task IntegrationEvent_With_Empty_Guid_Should_Generate_New_Id()
    {
        var ev = new TestIntegrationEvent { EventId = Guid.Empty };
        
        await _outbox.StoreAsync(ev, _transaction);
        
        await _repo.Received(1).InsertAsync(
            Arg.Is<OutboxMessage>(m => m.Id != Guid.Empty),
            _transaction,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TypeResolver_Throws_InvalidOperationException_Should_Fallback_To_TypeName()
    {
        _resolver.GetAlias(Arg.Any<Type>()).Returns(x => throw new InvalidOperationException());

        await _outbox.StoreAsync(new TestMessage(), _transaction);

        await _repo.Received(1).InsertAsync(
            Arg.Is<OutboxMessage>(m => m.MessageType == "TestMessage"),
            _transaction,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Publish_WithHeader_Should_Use_Resolved_Type_Alias()
    {
        _resolver.GetAlias(typeof(TestMessage)).Returns("custom.order.alias");

        await _outbox.Publish(new TestMessage())
            .WithTransaction(_transaction)
            .WithHeader("TenantId", "tenant-123")
            .StoreAsync();

        await _repo.Received(1).InsertAsync(
            Arg.Is<OutboxMessage>(m => m.MessageType == "custom.order.alias" && System.Text.Encoding.UTF8.GetString(m.Headers.ToArray()).Contains("tenant-123")),
            _transaction,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StoreAsync_With_ActivityBaggage_Should_Include_In_Headers()
    {
        using var activitySource = new System.Diagnostics.ActivitySource("Test");
        using var listener = new System.Diagnostics.ActivityListener
        {
            ShouldListenTo = s => s.Name == "Test",
            Sample = (ref System.Diagnostics.ActivityCreationOptions<System.Diagnostics.ActivityContext> options) => System.Diagnostics.ActivitySamplingResult.AllData
        };
        System.Diagnostics.ActivitySource.AddActivityListener(listener);

        using var activity = activitySource.StartActivity("TestActivity");
        activity?.AddBaggage("BaggageKey", "BaggageValue");
        activity?.AddBaggage("NullBaggageKey", null);

        var builder = _outbox.Publish(new TestMessage())
            .WithTransaction(_transaction)
            .WithHeader("BaggageKey", "OverriddenValue"); // duplicate to test metadata override

        await builder.StoreAsync();

        await _repo.Received(1).InsertAsync(
            Arg.Is<OutboxMessage>(m => 
                System.Text.Encoding.UTF8.GetString(m.Headers.ToArray()).Contains("OverriddenValue") && 
                !System.Text.Encoding.UTF8.GetString(m.Headers.ToArray()).Contains("BaggageValue") &&
                !System.Text.Encoding.UTF8.GetString(m.Headers.ToArray()).Contains("NullBaggageKey")),
            _transaction,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StoreAsync_With_ActivityBaggage_And_No_Metadata_Should_Include_In_Headers()
    {
        using var activitySource = new System.Diagnostics.ActivitySource("TestNoMetadata");
        using var listener = new System.Diagnostics.ActivityListener
        {
            ShouldListenTo = s => s.Name == "TestNoMetadata",
            Sample = (ref System.Diagnostics.ActivityCreationOptions<System.Diagnostics.ActivityContext> options) => System.Diagnostics.ActivitySamplingResult.AllData
        };
        System.Diagnostics.ActivitySource.AddActivityListener(listener);

        using var activity = activitySource.StartActivity("TestActivity");
        activity?.AddBaggage("OnlyBaggageKey", "OnlyBaggageValue");

        // Do not add headers, so metadata is empty
        await _outbox.Publish(new TestMessage())
            .WithTransaction(_transaction)
            .StoreAsync();

        await _repo.Received(1).InsertAsync(
            Arg.Is<OutboxMessage>(m => System.Text.Encoding.UTF8.GetString(m.Headers.ToArray()).Contains("OnlyBaggageValue")),
            _transaction,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Constructor_Should_Throw_On_Null_Dependencies()
    {
        var options = Microsoft.Extensions.Options.Options.Create(new EricksonLopez.Outbox.OutboxRuntimeOptions());
        Action act1 = () => _ = new DefaultOutbox(null!, _serializer, _resolver, options, new EricksonLopez.Outbox.Diagnostics.OutboxMetrics());
        act1.Should().Throw<ArgumentNullException>().WithParameterName("repository");

        Action act2 = () => _ = new DefaultOutbox(_repo, null!, _resolver, options, new EricksonLopez.Outbox.Diagnostics.OutboxMetrics());
        act2.Should().Throw<ArgumentNullException>().WithParameterName("serializer");

        Action act3 = () => _ = new DefaultOutbox(_repo, _serializer, null!, options, new EricksonLopez.Outbox.Diagnostics.OutboxMetrics());
        act3.Should().Throw<ArgumentNullException>().WithParameterName("typeResolver");

        Action act4 = () => _ = new DefaultOutbox(_repo, _serializer, _resolver, null!, new EricksonLopez.Outbox.Diagnostics.OutboxMetrics());
        act4.Should().Throw<ArgumentNullException>().WithParameterName("options");

        Action act5 = () => _ = new DefaultOutbox(_repo, _serializer, _resolver, options, null!);
        act5.Should().Throw<ArgumentNullException>().WithParameterName("metrics");
    }

    [Fact]
    public async Task StoreAsync_With_Empty_Messages_Should_Return_Default()
    {
        await _outbox.StoreAsync(ReadOnlyMemory<TestMessage>.Empty, _transaction);
        
        await _repo.DidNotReceiveWithAnyArgs().InsertBatchAsync(default!, default!, default!);
    }

    [Fact]
    public async Task TypeResolver_TryGetAlias_Returns_True_Should_Use_Resolved_Alias()
    {
        _resolver.TryGetAlias(typeof(TestMessage), out Arg.Any<string?>())
            .Returns(x =>
            {
                x[1] = "ResolvedAliasViaTryGet";
                return true;
            });

        await _outbox.StoreAsync(new TestMessage(), _transaction);

        await _repo.Received(1).InsertAsync(
            Arg.Is<OutboxMessage>(m => m.MessageType == "ResolvedAliasViaTryGet"),
            _transaction,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TypeResolver_TryGetAlias_Returns_True_But_Empty_String_Should_Fallback()
    {
        _resolver.TryGetAlias(typeof(TestMessage), out Arg.Any<string?>())
            .Returns(x =>
            {
                x[1] = "";
                return true;
            });
        
        // It should fallback to GetAlias
        await _outbox.StoreAsync(new TestMessage(), _transaction);

        await _repo.Received(1).InsertAsync(
            Arg.Is<OutboxMessage>(m => m.MessageType == "TestAlias"),
            _transaction,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OutboxMessageBuilder_ResizeHeaders_Should_Work()
    {
        var builder = _outbox.Publish(new TestMessage());
        builder = builder.WithTransaction(_transaction);
        
        // Add 10 headers to exceed the initial ArrayPool rent of 8 and trigger reallocation
        for (int i = 0; i < 10; i++)
        {
            builder = builder.WithHeader($"key{i}", $"val{i}");
        }
        
        await builder.StoreAsync();

        await _repo.Received(1).InsertAsync(
            Arg.Is<OutboxMessage>(m => 
                System.Text.Encoding.UTF8.GetString(m.Headers.ToArray()).Contains("key9") &&
                System.Text.Encoding.UTF8.GetString(m.Headers.ToArray()).Contains("val9")
            ),
            _transaction,
            Arg.Any<CancellationToken>());
    }

    public class TestMessage { public string Data { get; set; } = string.Empty; }
    public class TestIntegrationEvent : IIntegrationEvent 
    { 
        public Guid EventId { get; set; } 
        public DateTimeOffset OccurredOn { get; set; }
    }

    /// <summary>
    /// Deterministic fake serializer for DefaultOutbox unit tests.
    /// Always produces [1, 2, 3] bytes so Payload.Length == 3 assertions hold.
    /// NSubstitute cannot mock generic default interface methods (Serialize&lt;T&gt;),
    /// so a concrete fake is required.
    /// </summary>
    private sealed class FakeSerializer : IOutboxSerializer
    {
        private static readonly byte[] Bytes = [1, 2, 3];

        public ReadOnlyMemory<byte> Serialize<TMessage>(TMessage message) => Bytes;

        public void Serialize<TMessage>(TMessage message, System.Buffers.IBufferWriter<byte> buffer)
        {
            var span = buffer.GetSpan(3);
            span[0] = 1; span[1] = 2; span[2] = 3;
            buffer.Advance(3);
        }

        public TMessage Deserialize<TMessage>(ReadOnlySpan<byte> data) => default!;
    }

    [Fact]
    public async Task BuildOutboxMessage_Should_Fallback_To_ClassName_When_GetAlias_Throws()
    {
        var repo = Substitute.For<IOutboxRepository>();
        var serializer = Substitute.For<IOutboxSerializer>();
        var typeResolver = Substitute.For<IOutboxMessageTypeResolver>();
        
        typeResolver.TryGetAlias(typeof(TestMessage1), out _).Returns(false);
        typeResolver.GetAlias(typeof(TestMessage1)).Returns(x => throw new InvalidOperationException());

        var options = new OutboxRuntimeOptions
        {
            ThrowOnUnregisteredType = false
        };

        var outbox = new DefaultOutbox(repo, serializer, typeResolver, Options.Create(options), new EricksonLopez.Outbox.Diagnostics.OutboxMetrics());
        var msg = new TestMessage1();
        var tx = Substitute.For<EricksonLopez.Outbox.Persistence.IOutboxTransactionContext>();
        
        await outbox.StoreAsync(msg, tx);

        await repo.Received().InsertAsync(
            Arg.Is<OutboxMessage>(m => m.MessageType == "TestMessage1"), 
            tx, 
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ThrowOnUnregisteredType_True_Should_Throw_OutboxTypeNotRegisteredException()
    {
        var repo = Substitute.For<IOutboxRepository>();
        var serializer = Substitute.For<IOutboxSerializer>();
        var typeResolver = Substitute.For<IOutboxMessageTypeResolver>();
        typeResolver.GetAlias(Arg.Any<Type>()).Returns(x => throw new InvalidOperationException());
        typeResolver.TryGetAlias(Arg.Any<Type>(), out string? _).Returns(false);

        var options = new OutboxRuntimeOptions
        {
            ThrowOnUnregisteredType = true
        };

        var outbox = new DefaultOutbox(repo, serializer, typeResolver, Options.Create(options), new EricksonLopez.Outbox.Diagnostics.OutboxMetrics());
        var msg = new TestMessage1();
        var act = async () => await outbox.StoreAsync(msg, NSubstitute.Substitute.For<EricksonLopez.Outbox.Persistence.IOutboxTransactionContext>());
        await act.Should().ThrowAsync<OutboxTypeNotRegisteredException>();
    }

    [Fact]
    public async Task MaxPayloadSize_Exceeded_Should_Throw_InvalidOperationException()
    {
        var repo = Substitute.For<IOutboxRepository>();
        var serializer = Substitute.For<IOutboxSerializer>();
        var typeResolver = Substitute.For<IOutboxMessageTypeResolver>();
        typeResolver.TryGetAlias(typeof(TestMessage1), out string? _).Returns(x => { x[1] = "TestMessage1"; return true; });

        serializer.When(x => x.Serialize(Arg.Any<object>(), Arg.Any<System.Buffers.IBufferWriter<byte>>()))
                  .Do(info =>
                  {
                      var writer = info.Arg<System.Buffers.IBufferWriter<byte>>();
                      var span = writer.GetSpan(2000);
                      span.Clear();
                      writer.Advance(2000);
                  });

        var options = new OutboxRuntimeOptions
        {
            MaxPayloadSizeInBytes = 1000
        };

        var outbox = new DefaultOutbox(repo, serializer, typeResolver, Options.Create(options), new EricksonLopez.Outbox.Diagnostics.OutboxMetrics());
        var msg = new TestMessage1();
        var act = async () => await outbox.StoreAsync(msg, NSubstitute.Substitute.For<EricksonLopez.Outbox.Persistence.IOutboxTransactionContext>());
        await act.Should().ThrowAsync<OutboxPayloadTooLargeException>().WithMessage("*exceeds the configured maximum*");
    }
    [Fact]
    public async Task StoreAsync_WithReadOnlyMemory_StoresBatch()
    {
        var messages = new ReadOnlyMemory<TestMessage>(new[] { new TestMessage(), new TestMessage() });
        _repo.InsertBatchAsync(Arg.Any<ReadOnlyMemory<OutboxMessage>>(), _transaction, Arg.Any<CancellationToken>())
            .Returns(new ValueTask(Task.CompletedTask));

        await _outbox.StoreAsync(messages, _transaction);

        await _repo.Received(1).InsertBatchAsync(
            Arg.Is<ReadOnlyMemory<OutboxMessage>>(m => m.Length == 2),
            _transaction,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StoreAsync_WithMetadata_AndDeliverAt_StoresMessage()
    {
        var metadata = new MessageMetadata("corr-123", "caus-123", "MyType");
        var deliverAt = DateTimeOffset.UtcNow.AddMinutes(5);

        await _outbox.StoreAsync(new TestMessage(), _transaction, metadata, deliverAt);

        await _repo.Received(1).InsertAsync(
            Arg.Is<OutboxMessage>(m => 
                m.CorrelationId == "corr-123" && 
                m.CausationId == "caus-123" && 
                m.MessageType == "MyType" && 
                m.DeliverAt == deliverAt),
            _transaction,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StoreAsync_WithLargeHeaders_Throws_OutboxHeadersTooLargeException()
    {
        var options = new OutboxRuntimeOptions { MaxHeaderSizeInBytes = 50 };
        var outbox = new DefaultOutbox(_repo, _serializer, _resolver, Options.Create(options), new EricksonLopez.Outbox.Diagnostics.OutboxMetrics());

        var act = async () => await outbox.Publish(new TestMessage())
            .WithTransaction(_transaction)
            .WithHeader("VeryLargeHeaderKey", new string('A', 100))
            .StoreAsync();

        await act.Should().ThrowAsync<OutboxHeadersTooLargeException>();
    }

    [Fact]
    public async Task StoreAsync_WithBaggage_And_DuplicateMetadata_Should_Ignore_Baggage()
    {
        using var activitySource = new System.Diagnostics.ActivitySource("TestDuplicate");
        using var listener = new System.Diagnostics.ActivityListener
        {
            ShouldListenTo = s => s.Name == "TestDuplicate",
            Sample = (ref System.Diagnostics.ActivityCreationOptions<System.Diagnostics.ActivityContext> options) => System.Diagnostics.ActivitySamplingResult.AllData
        };
        System.Diagnostics.ActivitySource.AddActivityListener(listener);

        using var activity = activitySource.StartActivity("TestActivity");
        activity?.AddBaggage("TenantId", "BaggageTenant");

        var builder = _outbox.Publish(new TestMessage())
            .WithTransaction(_transaction)
            .WithHeader("TenantId", "ExplicitTenant");

        await builder.StoreAsync();

        await _repo.Received(1).InsertAsync(
            Arg.Is<OutboxMessage>(m => 
                System.Text.Encoding.UTF8.GetString(m.Headers.ToArray()).Contains("ExplicitTenant") &&
                !System.Text.Encoding.UTF8.GetString(m.Headers.ToArray()).Contains("BaggageTenant")),
            _transaction,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StoreAsync_WithLargePayload_Should_Dispose_Buffer()
    {
        var localSerializer = Substitute.For<IOutboxSerializer>();
        
        // Mock serializer to write > 64KB
        localSerializer.When(x => x.Serialize(Arg.Any<object>(), Arg.Any<System.Buffers.IBufferWriter<byte>>()))
            .Do(info =>
            {
                var writer = info.Arg<System.Buffers.IBufferWriter<byte>>();
                var span = writer.GetSpan(70000); // larger than 64KB
                span.Clear();
                writer.Advance(70000);
            });

        var options = new OutboxRuntimeOptions { MaxPayloadSizeInBytes = 100000 };
        var outbox = new DefaultOutbox(_repo, localSerializer, _resolver, Options.Create(options), new EricksonLopez.Outbox.Diagnostics.OutboxMetrics());

        await outbox.StoreAsync(new TestMessage(), _transaction);

        await _repo.Received(1).InsertAsync(
            Arg.Is<OutboxMessage>(m => m.Payload.Length == 70000),
            _transaction,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StoreAsync_WithMetadata_When_JsonWriterThrows_Should_Reset_Writer_And_Rethrow()
    {
        var outbox = new DefaultOutbox(_repo, _serializer, _resolver, Options.Create(new OutboxRuntimeOptions()), new EricksonLopez.Outbox.Diagnostics.OutboxMetrics());
        var metadata = new MessageMetadata("corr-123", "caus-123", "MyType", new[] { new MetadataEntry(null!, "val") }); // null key throws ArgumentNullException in Utf8JsonWriter
        
        var act = async () => await outbox.StoreAsync(new TestMessage(), _transaction, metadata, null);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task StoreAsync_With_EmptyActivityBaggage_Should_Not_Throw()
    {
        using var activitySource = new System.Diagnostics.ActivitySource("TestEmptyBaggage");
        using var listener = new System.Diagnostics.ActivityListener
        {
            ShouldListenTo = s => s.Name == "TestEmptyBaggage",
            Sample = (ref System.Diagnostics.ActivityCreationOptions<System.Diagnostics.ActivityContext> options) => System.Diagnostics.ActivitySamplingResult.AllData
        };
        System.Diagnostics.ActivitySource.AddActivityListener(listener);

        using var activity = activitySource.StartActivity("TestActivity");
        // We do NOT add baggage, so baggage is empty but not null

        await _outbox.StoreAsync(new TestMessage(), _transaction);

        await _repo.Received(1).InsertAsync(Arg.Any<OutboxMessage>(), _transaction, Arg.Any<CancellationToken>());
    }

    // --- P1-B FIX: deliver_at Dead Zone validation tests ---

    [Fact]
    public async Task StoreAsync_Should_Succeed_When_DeliverAt_Is_Within_MaxMessageAge()
    {
        // Arrange: deliver_at is 1 day from now; MaxMessageAge is 30 days → well within the window (1 < 30).
        var options = Microsoft.Extensions.Options.Options.Create(new OutboxRuntimeOptions { MaxMessageAge = TimeSpan.FromDays(30) });
        var outbox = new DefaultOutbox(_repo, _serializer, _resolver, options, new EricksonLopez.Outbox.Diagnostics.OutboxMetrics());
        var deliverAt = DateTimeOffset.UtcNow.AddDays(1); // clearly within the 30-day window

        // Act & Assert: should not throw
        await outbox
            .Publish(new TestMessage())
            .WithTransaction(_transaction)
            .WithDeliverAt(deliverAt)
            .StoreAsync(CancellationToken.None);

        await _repo.Received(1).InsertAsync(
            Arg.Is<OutboxMessage>(m => m.DeliverAt.HasValue),
            _transaction,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StoreAsync_Should_Throw_ArgumentOutOfRangeException_When_DeliverAt_Exceeds_MaxMessageAge()
    {
        // Arrange: deliver_at is clearly BEYOND MaxMessageAge — the classic P1-B silent loss scenario.
        // Example: scheduling 8 days ahead when MaxMessageAge = 7 days.
        // Use a fixed future date well beyond the boundary to avoid clock-tick flakiness.
        var maxAge = TimeSpan.FromDays(7);
        var options = Microsoft.Extensions.Options.Options.Create(new OutboxRuntimeOptions { MaxMessageAge = maxAge });
        var outbox = new DefaultOutbox(_repo, _serializer, _resolver, options, new EricksonLopez.Outbox.Diagnostics.OutboxMetrics());

        // Deterministic: 10 days from now when MaxMessageAge = 7 → clearly exceeds the boundary.
        var deliverAt = DateTimeOffset.UtcNow.AddDays(10);

        // Act
        Func<Task> act = async () => await outbox
            .Publish(new TestMessage())
            .WithTransaction(_transaction)
            .WithDeliverAt(deliverAt)
            .StoreAsync(CancellationToken.None);

        // Assert: must fail loudly before storing, not silently after storing.
        // ArgumentOutOfRangeException.ToString() contains the param name and the message param.
        await act.Should().ThrowAsync<ArgumentOutOfRangeException>()
            .WithMessage("*deliver_at*");

        // The repository must NOT have been called — the message is rejected before persistence.
        await _repo.DidNotReceive().InsertAsync(
            Arg.Any<OutboxMessage>(),
            Arg.Any<IOutboxTransactionContext>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StoreAsync_Should_Throw_ArgumentOutOfRangeException_And_Message_Contains_MaxMessageAge_Info()
    {
        // Verifies that the exception message includes actionable guidance (MaxMessageAge name and values).
        var maxAge = TimeSpan.FromDays(5);
        var options = Microsoft.Extensions.Options.Options.Create(new OutboxRuntimeOptions { MaxMessageAge = maxAge });
        var outbox = new DefaultOutbox(_repo, _serializer, _resolver, options, new EricksonLopez.Outbox.Diagnostics.OutboxMetrics());
        var deliverAt = DateTimeOffset.UtcNow.AddDays(20); // 20 days >> 5 day MaxMessageAge

        // Act
        ArgumentOutOfRangeException? caught = null;
        try
        {
            await outbox
                .Publish(new TestMessage())
                .WithTransaction(_transaction)
                .WithDeliverAt(deliverAt)
                .StoreAsync(CancellationToken.None);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            caught = ex;
        }

        // Assert exception was thrown with guidance text
        caught.Should().NotBeNull("StoreAsync must throw when deliver_at exceeds MaxMessageAge");
        caught!.Message.Should().Contain("MaxMessageAge",
            "the error message must instruct the user to increase MaxMessageAge");
        caught.Message.Should().Contain("deliver_at",
            "the error message must reference the problematic field");
        caught.ParamName.Should().Be("deliverAt");
    }

    [Fact]
    public async Task StoreAsync_Should_Throw_OutboxHeadersTooLargeException_When_Headers_Exceed_Limit()
    {
        var options = Microsoft.Extensions.Options.Options.Create(new OutboxRuntimeOptions { MaxHeaderSizeInBytes = 10 });
        var outbox = new DefaultOutbox(_repo, _serializer, _resolver, options, new EricksonLopez.Outbox.Diagnostics.OutboxMetrics());

        var metadata = new MessageMetadata(null, null, null, new[] { new MetadataEntry("HugeKey", new string('A', 20)) });

        Func<Task> act = async () => await outbox.StoreAsync(new TestMessage(), _transaction, metadata, null, CancellationToken.None);

        await act.Should().ThrowAsync<OutboxHeadersTooLargeException>();
    }
}




