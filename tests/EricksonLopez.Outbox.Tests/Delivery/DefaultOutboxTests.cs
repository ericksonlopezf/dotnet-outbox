// Copyright © Erickson Lopez. MIT License.
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using EricksonLopez.Outbox;
using EricksonLopez.Outbox.Contracts;
using EricksonLopez.Outbox.Persistence;
using EricksonLopez.Outbox.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

#pragma warning disable CA2012
namespace EricksonLopez.Outbox.Tests.Delivery;

public record TestMessage1;

[Collection("ActivitySource")]
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

        _resolver.TryGetAlias(Arg.Any<Type>(), out Arg.Any<string?>()).Returns(x => { x[1] = "TestAlias"; return true; });
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
    }

    [Fact]
    public async Task StoreAsync_Should_ThrowArgumentNullException_WhenTransactionIsNull()
    {
        var msg = new TestMessage { Data = "Hello" };
        var ex = await Assert.ThrowsAsync<ArgumentNullException>(() => _outbox.StoreAsync(msg, null!).AsTask());
        ex.ParamName.Should().Be("transaction");

        var metadata = new OutboxMessageMetadata(correlationId: null, causationId: null, messageType: null);
        var ex2 = await Assert.ThrowsAsync<ArgumentNullException>(() => _outbox.StoreAsync(msg, null!, metadata, deliverAt: null).AsTask());
        ex2.ParamName.Should().Be("transaction");

        var ex3 = await Assert.ThrowsAsync<ArgumentNullException>(() => _outbox.StoreAsync(new ReadOnlyMemory<TestMessage>([msg]), null!).AsTask());
        ex3.ParamName.Should().Be("transaction");
    }

    [Fact]
    public async Task StoreAsync_WhenDeliverAtIsExactlyAtMaxMessageAgeDeadline_Succeeds()
    {
        var fixedTime = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var fakeTime = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(fixedTime);
        var options = Microsoft.Extensions.Options.Options.Create(new EricksonLopez.Outbox.OutboxRuntimeOptions());
        var outbox = new DefaultOutbox(_repo, _serializer, _resolver, options, new EricksonLopez.Outbox.Diagnostics.OutboxMetrics(), fakeTime);

        var msg = new TestMessage { Data = "Hello" };
        var metadata = new OutboxMessageMetadata(correlationId: null, causationId: null, messageType: null);
        var exactDeadline = fixedTime.Add(options.Value.MaxMessageAge);

        var action = () => outbox.StoreAsync(msg, _transaction, metadata, deliverAt: exactDeadline).AsTask();
        await action.Should().NotThrowAsync();

        var overDeadline = exactDeadline.AddTicks(1);
        var overAction = () => outbox.StoreAsync(msg, _transaction, metadata, deliverAt: overDeadline).AsTask();
        await overAction.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task StoreAsync_WhenRepositoryAsync_AwaitsPersistenceAndRecordsMetrics()
    {
        var msg = new TestMessage { Data = "Hello" };
        var repo = Substitute.For<IOutboxRepository>();
        var metrics = new EricksonLopez.Outbox.Diagnostics.OutboxMetrics();
        var options = Microsoft.Extensions.Options.Options.Create(new EricksonLopez.Outbox.OutboxRuntimeOptions());
        var outbox = new DefaultOutbox(repo, _serializer, _resolver, options, metrics);

        double recordedDuration = -1;
        using var listener = new System.Diagnostics.Metrics.MeterListener();
        listener.InstrumentPublished = (inst, l) =>
        {
            if (inst.Meter.Name == EricksonLopez.Outbox.Diagnostics.OutboxMetrics.MeterName && inst.Name == "messaging.outbox.store.duration")
                l.EnableMeasurementEvents(inst);
        };
        listener.SetMeasurementEventCallback<double>((inst, val, tags, state) =>
        {
            recordedDuration = val;
        });
        listener.Start();

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        repo.InsertAsync(Arg.Any<OutboxMessage>(), Arg.Any<IOutboxTransactionContext>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask(tcs.Task));

        var storeTask = outbox.StoreAsync(msg, _transaction);
        storeTask.IsCompleted.Should().BeFalse();
        recordedDuration.Should().Be(-1, "metrics must NOT be recorded before async persistence completes");

        tcs.SetResult(true);
        await storeTask.AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        recordedDuration.Should().BeGreaterThanOrEqualTo(0, "metrics must be recorded after async persistence completes");
        await repo.Received(1).InsertAsync(Arg.Any<OutboxMessage>(), _transaction, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StoreAsync_Batch_Should_Build_And_Insert_Multiple_Messages()
    {
        var msgs = new[] { new TestMessage { Data = "A" }, new TestMessage { Data = "B" } };
        ReadOnlyMemory<OutboxMessage> capturedBatch = default;
        _repo.InsertBatchAsync(Arg.Any<ReadOnlyMemory<OutboxMessage>>(), _transaction, Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var mem = ci.Arg<ReadOnlyMemory<OutboxMessage>>();
                capturedBatch = mem.ToArray();
                return default;
            });

        await _outbox.StoreAsync<TestMessage>(msgs, _transaction);

        capturedBatch.Length.Should().Be(2);
        capturedBatch.Span[0].MessageType.Should().Be("TestAlias");
        capturedBatch.Span[1].MessageType.Should().Be("TestAlias");
        capturedBatch.Span[0].Payload.Length.Should().Be(3);
    }

    [Fact]
    public async Task StoreAsync_Batch_WhenTransactionIsNull_ThrowsArgumentNullException()
    {
        var msgs = new[] { new TestMessage { Data = "A" }, new TestMessage { Data = "B" } };
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
    public async Task StoreAsync_Should_Generate_Valid_Id()
    {
        var ev = new TestIntegrationEvent { EventId = Guid.NewGuid() };
        
        await _outbox.StoreAsync(ev, _transaction);
        
        await _repo.Received(1).InsertAsync(
            Arg.Is<OutboxMessage>(m => m.Id != Guid.Empty),
            _transaction,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StoreAsync_With_Metadata_Should_Map_Properly()
    {
        var metadata = new OutboxMessageMetadata("corr-1", "caus-1", "CustomType", new[] { new MetadataEntry("key", "val") });
        
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
        var options = Microsoft.Extensions.Options.Options.Create(new EricksonLopez.Outbox.OutboxRuntimeOptions { ThrowOnUnregisteredType = false });
        var outbox = new DefaultOutbox(_repo, _serializer, _resolver, options, new EricksonLopez.Outbox.Diagnostics.OutboxMetrics());

        _resolver.TryGetAlias(Arg.Any<Type>(), out Arg.Any<string?>()).Returns(false);
        _resolver.GetAlias(Arg.Any<Type>()).Returns(x => throw new InvalidOperationException());

        await outbox.StoreAsync(new TestMessage(), _transaction);

        await _repo.Received(1).InsertAsync(
            Arg.Is<OutboxMessage>(m => m.MessageType == "TestMessage"),
            _transaction,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Publish_WithHeader_Should_Use_Resolved_Type_Alias()
    {
        _resolver.TryGetAlias(typeof(TestMessage), out Arg.Any<string?>()).Returns(x => { x[1] = "custom.order.alias"; return true; });
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
    public void Constructor_WhenRepositoryIsNull_ThrowsArgumentNullException()
    {
        var options = Microsoft.Extensions.Options.Options.Create(new EricksonLopez.Outbox.OutboxRuntimeOptions());
        Action act = () => _ = new DefaultOutbox(null!, _serializer, _resolver, options, new EricksonLopez.Outbox.Diagnostics.OutboxMetrics());
        act.Should().Throw<ArgumentNullException>().WithParameterName("repository");
    }

    [Fact]
    public void Constructor_WhenSerializerIsNull_ThrowsArgumentNullException()
    {
        var options = Microsoft.Extensions.Options.Options.Create(new EricksonLopez.Outbox.OutboxRuntimeOptions());
        Action act = () => _ = new DefaultOutbox(_repo, null!, _resolver, options, new EricksonLopez.Outbox.Diagnostics.OutboxMetrics());
        act.Should().Throw<ArgumentNullException>().WithParameterName("serializer");
    }

    [Fact]
    public void Constructor_WhenTypeResolverIsNull_ThrowsArgumentNullException()
    {
        var options = Microsoft.Extensions.Options.Options.Create(new EricksonLopez.Outbox.OutboxRuntimeOptions());
        Action act = () => _ = new DefaultOutbox(_repo, _serializer, null!, options, new EricksonLopez.Outbox.Diagnostics.OutboxMetrics());
        act.Should().Throw<ArgumentNullException>().WithParameterName("typeResolver");
    }

    [Fact]
    public void Constructor_WhenOptionsIsNull_ThrowsArgumentNullException()
    {
        Action act = () => _ = new DefaultOutbox(_repo, _serializer, _resolver, null!, new EricksonLopez.Outbox.Diagnostics.OutboxMetrics());
        act.Should().Throw<ArgumentNullException>().WithParameterName("options");
    }

    [Fact]
    public void Constructor_WhenMetricsIsNull_ThrowsArgumentNullException()
    {
        var options = Microsoft.Extensions.Options.Options.Create(new EricksonLopez.Outbox.OutboxRuntimeOptions());
        Action act = () => _ = new DefaultOutbox(_repo, _serializer, _resolver, options, null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("metrics");
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
        var options = Microsoft.Extensions.Options.Options.Create(new EricksonLopez.Outbox.OutboxRuntimeOptions { ThrowOnUnregisteredType = false });
        var outbox = new DefaultOutbox(_repo, _serializer, _resolver, options, new EricksonLopez.Outbox.Diagnostics.OutboxMetrics());

        _resolver.TryGetAlias(typeof(TestMessage), out Arg.Any<string?>())
            .Returns(x =>
            {
                x[1] = "";
                return true;
            });
        
        // It should fallback to GetAlias when ThrowOnUnregisteredType is false
        await outbox.StoreAsync(new TestMessage(), _transaction);

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
    public class TestIntegrationEvent 
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
    public async Task StoreAsync_WithReadOnlyMemory_Should_StoreBatch()
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
        var metadata = new OutboxMessageMetadata("corr-123", "caus-123", "MyType");
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
        var metadata = new OutboxMessageMetadata("corr-123", "caus-123", "MyType", new[] { new MetadataEntry(null!, "val") }); // null key throws ArgumentNullException in Utf8JsonWriter
        
        var act = async () => await outbox.StoreAsync(new TestMessage(), _transaction, metadata, null);
        await act.Should().ThrowAsync<ArgumentNullException>();

        // Verify writer was cleanly reset and subsequent call succeeds
        var validMetadata = new OutboxMessageMetadata("corr-123", "caus-123", "MyType", new[] { new MetadataEntry("validKey", "val") });
        await outbox.StoreAsync(new TestMessage(), _transaction, validMetadata, null);
        await _repo.Received(1).InsertAsync(
            Arg.Is<OutboxMessage>(m => System.Text.Encoding.UTF8.GetString(m.Headers.ToArray()).Contains("validKey")),
            _transaction,
            Arg.Any<CancellationToken>());
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

        var metadata = new OutboxMessageMetadata(null, null, null, new[] { new MetadataEntry("HugeKey", new string('A', 20)) });

        Func<Task> act = async () => await outbox.StoreAsync(new TestMessage(), _transaction, metadata, null, CancellationToken.None);

        await act.Should().ThrowAsync<OutboxHeadersTooLargeException>();
    }

    [Fact]
    public async Task MaxPayloadSize_ExactLimit_Should_Succeed()
    {
        var repo = Substitute.For<IOutboxRepository>();
        var serializer = Substitute.For<IOutboxSerializer>();
        var typeResolver = Substitute.For<IOutboxMessageTypeResolver>();
        typeResolver.TryGetAlias(typeof(TestMessage1), out string? _).Returns(x => { x[1] = "TestMessage1"; return true; });

        serializer.When(x => x.Serialize(Arg.Any<object>(), Arg.Any<System.Buffers.IBufferWriter<byte>>()))
                  .Do(info =>
                  {
                      var writer = info.Arg<System.Buffers.IBufferWriter<byte>>();
                      var span = writer.GetSpan(1000);
                      span.Clear();
                      writer.Advance(1000);
                  });

        var options = new OutboxRuntimeOptions
        {
            MaxPayloadSizeInBytes = 1000
        };

        var outbox = new DefaultOutbox(repo, serializer, typeResolver, Options.Create(options), new EricksonLopez.Outbox.Diagnostics.OutboxMetrics());
        var msg = new TestMessage1();
        await outbox.StoreAsync(msg, _transaction);

        await repo.Received(1).InsertAsync(Arg.Is<OutboxMessage>(m => m.Payload.Length == 1000), _transaction, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StoreAsync_Should_Throw_When_DeliverAt_Is_Exact_Deadline()
    {
        var fixedTime = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var fakeTime = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(fixedTime);
        var maxAge = TimeSpan.FromDays(5);
        var options = Microsoft.Extensions.Options.Options.Create(new OutboxRuntimeOptions { MaxMessageAge = maxAge });
        var outbox = new DefaultOutbox(_repo, _serializer, _resolver, options, new EricksonLopez.Outbox.Diagnostics.OutboxMetrics(), fakeTime);
        var deliverAt = fixedTime.Add(maxAge).AddMilliseconds(50);

        Func<Task> act = async () => await outbox
            .Publish(new TestMessage())
            .WithTransaction(_transaction)
            .WithDeliverAt(deliverAt)
            .StoreAsync(CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task StoreAsync_WhenCalledSequentially_ClearsPreviousMessageHeaders()
    {
        var metadata1 = new OutboxMessageMetadata(null, null, null, new[] { new MetadataEntry("K1", "V1") });
        await _outbox.StoreAsync(new TestMessage(), _transaction, metadata1, null);

        var metadata2 = new OutboxMessageMetadata(null, null, null, new[] { new MetadataEntry("K2", "V2") });
        await _outbox.StoreAsync(new TestMessage(), _transaction, metadata2, null);

        await _repo.Received(1).InsertAsync(
            Arg.Is<OutboxMessage>(m => System.Text.Encoding.UTF8.GetString(m.Headers.ToArray()).Contains("K2") && !System.Text.Encoding.UTF8.GetString(m.Headers.ToArray()).Contains("K1")),
            _transaction,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StoreAsync_Should_Succeed_When_Headers_Equal_MaxHeaderSize()
    {
        // {"k":"v"} is 9 bytes in UTF8 JSON
        var options = Microsoft.Extensions.Options.Options.Create(new OutboxRuntimeOptions { MaxHeaderSizeInBytes = 9 });
        var outbox = new DefaultOutbox(_repo, _serializer, _resolver, options, new EricksonLopez.Outbox.Diagnostics.OutboxMetrics());

        var metadata = new OutboxMessageMetadata(null, null, null, new[] { new MetadataEntry("k", "v") });

        await outbox.StoreAsync(new TestMessage(), _transaction, metadata, null, CancellationToken.None);

        await _repo.Received().InsertAsync(Arg.Is<OutboxMessage>(m => m.Headers.Length == 9), _transaction, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StoreAsync_Should_Succeed_When_DeliverAt_Is_At_Deadline()
    {
        var maxAge = TimeSpan.FromDays(5);
        var options = Microsoft.Extensions.Options.Options.Create(new OutboxRuntimeOptions { MaxMessageAge = maxAge });
        var outbox = new DefaultOutbox(_repo, _serializer, _resolver, options, new EricksonLopez.Outbox.Diagnostics.OutboxMetrics());
        var deliverAt = DateTimeOffset.UtcNow.Add(maxAge).AddMilliseconds(-100);

        await outbox
            .Publish(new TestMessage())
            .WithTransaction(_transaction)
            .WithDeliverAt(deliverAt)
            .StoreAsync(CancellationToken.None);

        await _repo.Received(1).InsertAsync(Arg.Any<OutboxMessage>(), _transaction, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StoreAsync_WhenPayloadExceeds64KBCapacity_Should_Dispose_And_Reset_BufferWriter()
    {
        var repo = Substitute.For<IOutboxRepository>();
        var serializer = Substitute.For<IOutboxSerializer>();
        var typeResolver = Substitute.For<IOutboxMessageTypeResolver>();
        typeResolver.TryGetAlias(typeof(TestMessage1), out string? _).Returns(x => { x[1] = "TestMessage1"; return true; });

        EricksonLopez.Outbox.Serialization.ArrayPoolBufferWriter<byte>? firstWriter = null;
        EricksonLopez.Outbox.Serialization.ArrayPoolBufferWriter<byte>? secondWriter = null;

        serializer.When(x => x.Serialize(Arg.Any<object>(), Arg.Any<System.Buffers.IBufferWriter<byte>>()))
                  .Do(info =>
                  {
                      var writer = (EricksonLopez.Outbox.Serialization.ArrayPoolBufferWriter<byte>)info.Arg<System.Buffers.IBufferWriter<byte>>();
                      if (firstWriter == null)
                      {
                          firstWriter = writer;
                          // Advance 70,000 bytes so capacity > 65536
                          var span = writer.GetSpan(70000);
                          span.Clear();
                          writer.Advance(70000);
                      }
                      else
                      {
                          secondWriter = writer;
                          var span = writer.GetSpan(10);
                          writer.Advance(10);
                      }
                  });

        var options = new OutboxRuntimeOptions
        {
            MaxPayloadSizeInBytes = 100_000
        };

        var outbox = new DefaultOutbox(repo, serializer, typeResolver, Options.Create(options), new EricksonLopez.Outbox.Diagnostics.OutboxMetrics());
        
        // First call: expands capacity > 64KB -> disposes and sets t_payloadBufferWriter = null
        await outbox.StoreAsync(new TestMessage1(), _transaction);

        // Second call: creates a new buffer with default 1024 capacity
        await outbox.StoreAsync(new TestMessage1(), _transaction);

        var act = () => firstWriter!.Capacity;
        act.Should().Throw<NullReferenceException>("oversized buffer must have been disposed and cleared");

        secondWriter.Should().NotBeNull();
        secondWriter.Should().NotBeSameAs(firstWriter);
        secondWriter!.Capacity.Should().Be(1024);
    }

    [Fact]
    public async Task StoreAsync_WhenPayloadCapacityIsExact64KB_Should_Retain_BufferWriter()
    {
        var repo = Substitute.For<IOutboxRepository>();
        var serializer = Substitute.For<IOutboxSerializer>();
        var typeResolver = Substitute.For<IOutboxMessageTypeResolver>();
        typeResolver.TryGetAlias(typeof(TestMessage1), out string? _).Returns(x => { x[1] = "TestMessage1"; return true; });

        EricksonLopez.Outbox.Serialization.ArrayPoolBufferWriter<byte>? firstWriter = null;
        EricksonLopez.Outbox.Serialization.ArrayPoolBufferWriter<byte>? secondWriter = null;

        serializer.When(x => x.Serialize(Arg.Any<object>(), Arg.Any<System.Buffers.IBufferWriter<byte>>()))
                  .Do(info =>
                  {
                      var writer = (EricksonLopez.Outbox.Serialization.ArrayPoolBufferWriter<byte>)info.Arg<System.Buffers.IBufferWriter<byte>>();
                      if (firstWriter == null)
                      {
                          firstWriter = writer;
                          // Advance 32,769 bytes so capacity is exactly 65,536 (not strictly greater than 65536)
                          var span = writer.GetSpan(32769);
                          writer.Advance(32769);
                      }
                      else
                      {
                          secondWriter = writer;
                          var span = writer.GetSpan(10);
                          writer.Advance(10);
                      }
                  });

        var options = new OutboxRuntimeOptions
        {
            MaxPayloadSizeInBytes = 100_000
        };

        var outbox = new DefaultOutbox(repo, serializer, typeResolver, Options.Create(options), new EricksonLopez.Outbox.Diagnostics.OutboxMetrics());
        
        // First call: capacity is exactly 65536 (<= 65536) -> retained
        await outbox.StoreAsync(new TestMessage1(), _transaction);

        // Second call: reuses the same buffer
        await outbox.StoreAsync(new TestMessage1(), _transaction);

        secondWriter.Should().BeSameAs(firstWriter);
    }

    [Fact]
    public async Task StoreAsync_SequentialCalls_Reuses_PayloadBufferWriter()
    {
        var repo = Substitute.For<IOutboxRepository>();
        var serializer = Substitute.For<IOutboxSerializer>();
        var typeResolver = Substitute.For<IOutboxMessageTypeResolver>();
        typeResolver.TryGetAlias(typeof(TestMessage1), out string? _).Returns(x => { x[1] = "TestMessage1"; return true; });

        IBufferWriter<byte>? writer1 = null;
        IBufferWriter<byte>? writer2 = null;

        serializer.When(s => s.Serialize(Arg.Any<object>(), Arg.Any<System.Buffers.IBufferWriter<byte>>()))
                  .Do(ci =>
                  {
                      if (writer1 == null) writer1 = ci.Arg<System.Buffers.IBufferWriter<byte>>();
                      else writer2 = ci.Arg<System.Buffers.IBufferWriter<byte>>();
                      ci.Arg<System.Buffers.IBufferWriter<byte>>().GetSpan(10);
                      ci.Arg<System.Buffers.IBufferWriter<byte>>().Advance(10);
                  });

        var outbox = new DefaultOutbox(repo, serializer, typeResolver, Options.Create(new OutboxRuntimeOptions()), new EricksonLopez.Outbox.Diagnostics.OutboxMetrics());

        await outbox.StoreAsync(new TestMessage1(), _transaction);
        await outbox.StoreAsync(new TestMessage1(), _transaction);

        writer2.Should().BeSameAs(writer1);
    }

    [Fact]
    public async Task StoreAsync_HighConcurrency_Stress_ThreadStaticBuffers_MaintainsDataIntegrity()
    {
        var repo = Substitute.For<IOutboxRepository>();
        var storedMessages = new System.Collections.Concurrent.ConcurrentBag<OutboxMessage>();
        repo.InsertAsync(Arg.Do<OutboxMessage>(m => storedMessages.Add(m)), Arg.Any<IOutboxTransactionContext>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.CompletedTask);

        var dynamicSerializer = new ConcurrentStressSerializer();
        var typeResolver = Substitute.For<IOutboxMessageTypeResolver>();
        typeResolver.TryGetAlias(typeof(TestStressMessage), out Arg.Any<string?>()).Returns(x => { x[1] = "TestStressMessage"; return true; });
        typeResolver.GetAlias(typeof(TestStressMessage)).Returns("TestStressMessage");

        var outbox = new DefaultOutbox(repo, dynamicSerializer, typeResolver, Options.Create(new OutboxRuntimeOptions()), new EricksonLopez.Outbox.Diagnostics.OutboxMetrics());

        const int totalItems = 1000;
        var items = Enumerable.Range(0, totalItems).Select(i => new TestStressMessage(i, $"Data-{i}")).ToArray();

        var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount * 4 };
        await Parallel.ForEachAsync(items, parallelOptions, async (item, ct) =>
        {
            await outbox.StoreAsync(item, _transaction, cancellationToken: ct);
        });

        storedMessages.Count.Should().Be(totalItems);

        // Verify data integrity: each message payload matches its index with zero cross-thread buffer pollution
        var receivedIndices = new HashSet<int>();
        foreach (var msg in storedMessages)
        {
            msg.MessageType.Should().Be("TestStressMessage");
            var span = msg.Payload.Span;
            span.Length.Should().Be(4);
            int index = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(span);
            receivedIndices.Add(index).Should().BeTrue();
        }
        receivedIndices.Count.Should().Be(totalItems);
    }

    [Fact]
    public async Task StoreAsync_WhenSerializerThrows_PropagatesException()
    {
        var throwingSerializer = Substitute.For<IOutboxSerializer>();
        throwingSerializer.When(x => x.Serialize(Arg.Any<TestMessage>(), Arg.Any<IBufferWriter<byte>>()))
            .Do(_ => throw new InvalidOperationException("Custom serializer error"));

        var options = Microsoft.Extensions.Options.Options.Create(new OutboxRuntimeOptions());
        var outbox = new DefaultOutbox(_repo, throwingSerializer, _resolver, options, new EricksonLopez.Outbox.Diagnostics.OutboxMetrics());

        var act = async () => await outbox.StoreAsync(new TestMessage { Data = "Test" }, _transaction);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Custom serializer error");
        await _repo.DidNotReceiveWithAnyArgs().InsertAsync(default!, default!, default);
    }

    [Fact]
    public async Task StoreAsync_Batch_WhenSerializerThrows_PropagatesException()
    {
        var throwingSerializer = Substitute.For<IOutboxSerializer>();
        throwingSerializer.When(x => x.Serialize(Arg.Any<TestMessage>(), Arg.Any<IBufferWriter<byte>>()))
            .Do(_ => throw new InvalidOperationException("Batch serializer error"));

        var options = Microsoft.Extensions.Options.Options.Create(new OutboxRuntimeOptions());
        var outbox = new DefaultOutbox(_repo, throwingSerializer, _resolver, options, new EricksonLopez.Outbox.Diagnostics.OutboxMetrics());

        var msgs = new ReadOnlyMemory<TestMessage>([new TestMessage { Data = "Msg1" }, new TestMessage { Data = "Msg2" }]);
        var act = async () => await outbox.StoreAsync(msgs, _transaction);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Batch serializer error");
        await _repo.DidNotReceiveWithAnyArgs().InsertAsync(default!, default!, default);
    }

    private sealed record TestStressMessage(int Id, string Payload);

    private sealed class ConcurrentStressSerializer : IOutboxSerializer
    {
        public ReadOnlyMemory<byte> Serialize<TMessage>(TMessage message)
        {
            if (message is TestStressMessage stress)
            {
                var bytes = new byte[4];
                System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(bytes, stress.Id);
                return bytes;
            }
            return new byte[] { 1, 2, 3 };
        }

        public void Serialize<TMessage>(TMessage message, System.Buffers.IBufferWriter<byte> buffer)
        {
            if (message is TestStressMessage stress)
            {
                var span = buffer.GetSpan(4);
                System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(span, stress.Id);
                buffer.Advance(4);
                return;
            }
            var fallbackSpan = buffer.GetSpan(3);
            fallbackSpan[0] = 1; fallbackSpan[1] = 2; fallbackSpan[2] = 3;
            buffer.Advance(3);
        }

        public TMessage Deserialize<TMessage>(ReadOnlySpan<byte> data) => default!;
    }

    private static bool CheckBatch(ReadOnlyMemory<OutboxMessage> m)
    {
        var span = m.Span;
        return span.Length == 2 &&
               span[0].MessageType == "TestAlias" &&
               span[1].MessageType == "TestAlias" &&
               span[0].Payload.Length == 3 &&
               span[1].Payload.Length == 3;
    }
}









