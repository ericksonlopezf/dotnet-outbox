// Copyright © Erickson Lopez. MIT License.
#pragma warning disable CA2012, CS8600, CS8602
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using EricksonLopez.Outbox;
using EricksonLopez.Outbox.Diagnostics;
using EricksonLopez.Outbox.Dispatcher;
using EricksonLopez.Outbox.Persistence;
using EricksonLopez.Outbox.Serialization;
using EricksonLopez.Outbox.Tests.Infrastructure;
using EricksonLopez.Result;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace EricksonLopez.Outbox.Tests.Delivery;

public partial class OutboxChannelTests
{
    [Collection("ActivitySource")]
    public class PayloadAndHeadersTests
    {
        private readonly OutboxChannel _channel;
        private readonly IBrokerPublisher _publisher = Substitute.For<IBrokerPublisher>();
        private readonly IOutboxRepository _repository = Substitute.For<IOutboxRepository>();
        private readonly IDeadLetterRepository _dlqRepository = Substitute.For<IDeadLetterRepository>();
        private readonly IServiceScopeFactory _scopeFactory = Substitute.For<IServiceScopeFactory>();
        private readonly IServiceScope _scope = Substitute.For<IServiceScope>();
        private readonly IServiceProvider _serviceProvider = Substitute.For<IServiceProvider>();

        public PayloadAndHeadersTests()
        {
            _scopeFactory.CreateScope().Returns(_scope);
            _scope.ServiceProvider.Returns(_serviceProvider);
            _serviceProvider.GetService(typeof(IOutboxRepository)).Returns(_repository);
            _serviceProvider.GetService(typeof(IDeadLetterRepository)).Returns(_dlqRepository);
            _serviceProvider.GetService(typeof(IEnumerable<Pipeline.IOutboxMiddleware>)).Returns(Array.Empty<Pipeline.IOutboxMiddleware>());

            var options = Options.Create(new OutboxDispatcherOptions { ChannelCapacity = 10 });
            var runtimeOptions = Options.Create(new OutboxRuntimeOptions());

            _channel = new OutboxChannel(
                NullLogger<OutboxChannel>.Instance,
                _publisher,
                options,
                runtimeOptions,
                new OutboxMetrics(Substitute.For<System.Diagnostics.Metrics.IMeterFactory>()),
                _scopeFactory,
                new DefaultErrorSanitizer(),
                TimeProvider.System
            );
        }

        [Fact]
        public async Task ProcessMessagesAsync_WithNullHeaderValue_IgnoresHeader()
        {
            var jsonHeaders = "{\"key1\":null, \"key2\":\"value2\"}";
            var msg = new OutboxMessageTestDataBuilder()
                .WithMessageType("Type")
                .WithPayload(ReadOnlyMemory<byte>.Empty)
                .WithHeaders(JsonSerializer.SerializeToUtf8Bytes(JsonDocument.Parse(jsonHeaders).RootElement))
                .Build();
            
            _publisher.PublishRawAsync(Arg.Any<OutboxMessage>(), Arg.Any<OutboxMessageMetadata>(), Arg.Any<DispatchContext>())
                .Returns(DispatchResult.Ok());

            await _channel.WriteAsync(msg, CancellationToken.None);
            _channel.Complete();

            await _channel.ProcessMessagesAsync(CancellationToken.None);

            await _publisher.Received(1).PublishRawAsync(Arg.Any<OutboxMessage>(), Arg.Any<OutboxMessageMetadata>(), Arg.Any<DispatchContext>());
        }

        [Fact]
        public async Task ProcessMessagesAsync_WithInvalidJsonHeaders_FailsFatal()
        {
            var jsonHeaders = "invalid json";
            var msg = new OutboxMessageTestDataBuilder()
                .WithMessageType("Type")
                .WithPayload(ReadOnlyMemory<byte>.Empty)
                .WithHeaders(System.Text.Encoding.UTF8.GetBytes(jsonHeaders))
                .Build();
            
            await _channel.WriteAsync(msg, CancellationToken.None);
            _channel.Complete();

            await _channel.ProcessMessagesAsync(CancellationToken.None);

            await _repository.Received(1).MarkAsFailedAsync(msg, Arg.Is<string>(s => s.Contains("deserialize headers")), true, Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task ProcessMessagesAsync_WithJsonArrayHeaders_IgnoresHeaders()
        {
            var jsonHeaders = "[1, 2, 3]";
            var msg = new OutboxMessageTestDataBuilder()
                .WithMessageType("Type")
                .WithPayload(ReadOnlyMemory<byte>.Empty)
                .WithHeaders(System.Text.Encoding.UTF8.GetBytes(jsonHeaders))
                .Build();
            
            _publisher.PublishRawAsync(Arg.Any<OutboxMessage>(), Arg.Any<OutboxMessageMetadata>(), Arg.Any<DispatchContext>())
                .Returns(DispatchResult.Ok());

            await _channel.WriteAsync(msg, CancellationToken.None);
            _channel.Complete();

            await _channel.ProcessMessagesAsync(CancellationToken.None);

            await _publisher.Received(1).PublishRawAsync(Arg.Any<OutboxMessage>(), Arg.Any<OutboxMessageMetadata>(), Arg.Any<DispatchContext>());
        }

        [Fact]
        public async Task ProcessMessagesAsync_WithEmptyJsonHeaders_Works()
        {
            var jsonHeaders = "{}";
            var msg = new OutboxMessageTestDataBuilder()
                .WithMessageType("Type")
                .WithPayload(ReadOnlyMemory<byte>.Empty)
                .WithHeaders(System.Text.Encoding.UTF8.GetBytes(jsonHeaders))
                .Build();
            
            _publisher.PublishRawAsync(Arg.Any<OutboxMessage>(), Arg.Any<OutboxMessageMetadata>(), Arg.Any<DispatchContext>())
                .Returns(DispatchResult.Ok());

            await _channel.WriteAsync(msg, CancellationToken.None);
            _channel.Complete();

            await _channel.ProcessMessagesAsync(CancellationToken.None);

            await _publisher.Received(1).PublishRawAsync(Arg.Any<OutboxMessage>(), Arg.Any<OutboxMessageMetadata>(), Arg.Any<DispatchContext>());
        }

        [Fact]
        public async Task ProcessMessagesAsync_PayloadTooLarge_FailsFatal()
        {
            var largePayload = new byte[1024 * 1024 + 1]; // 1MB + 1
            var msg = new OutboxMessage(Guid.NewGuid(), "OrderCreated", largePayload, null, null, ReadOnlyMemory<byte>.Empty, DateTimeOffset.UtcNow, null, null, OutboxMessageStatus.Pending, 0, null);

            await _channel.WriteAsync(msg, CancellationToken.None);
            _channel.Complete();

            await _channel.ProcessMessagesAsync(CancellationToken.None);

            await _repository.Received(1).MarkAsFailedAsync(msg, Arg.Is<string>(s => s.Contains("Payload size")), true, Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task ProcessMessagesAsync_ExactMaxPayloadSize_Succeeds()
        {
            var exactPayload = new byte[1024 * 1024]; // Exactly 1MB
            var msg = new OutboxMessage(Guid.NewGuid(), "OrderCreated", exactPayload, null, null, ReadOnlyMemory<byte>.Empty, DateTimeOffset.UtcNow, null, null, OutboxMessageStatus.Pending, 0, null);
            _publisher.PublishRawAsync(Arg.Any<OutboxMessage>(), Arg.Any<OutboxMessageMetadata>(), Arg.Any<DispatchContext>())
                .Returns(DispatchResult.Ok());

            await _channel.WriteAsync(msg, CancellationToken.None);
            _channel.Complete();

            await _channel.ProcessMessagesAsync(CancellationToken.None);

            await _publisher.Received(1).PublishRawAsync(Arg.Any<OutboxMessage>(), Arg.Any<OutboxMessageMetadata>(), Arg.Any<DispatchContext>());
        }

        [Fact]
        public async Task ProcessMessagesAsync_HeadersTooLarge_FailsFatal()
        {
            var largeHeaders = new byte[64 * 1024 + 1]; // 64KB + 1
            var msg = new OutboxMessage(Guid.NewGuid(), "OrderCreated", ReadOnlyMemory<byte>.Empty, null, null, largeHeaders, DateTimeOffset.UtcNow, null, null, OutboxMessageStatus.Pending, 0, null);

            await _channel.WriteAsync(msg, CancellationToken.None);
            _channel.Complete();

            await _channel.ProcessMessagesAsync(CancellationToken.None);

            await _repository.Received(1).MarkAsFailedAsync(msg, Arg.Is<string>(s => s.Contains("Headers size")), true, Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task ProcessMessagesAsync_HeaderCaching_SequentialSameHeaders()
        {
            var headerBytes = System.Text.Encoding.UTF8.GetBytes("{\"k1\":\"v1\"}");
            var msg1 = new OutboxMessage(Guid.NewGuid(), "Type", ReadOnlyMemory<byte>.Empty, null, null, headerBytes, DateTimeOffset.UtcNow, null, null, OutboxMessageStatus.Pending, 0, null);
            var msg2 = new OutboxMessage(Guid.NewGuid(), "Type", ReadOnlyMemory<byte>.Empty, null, null, headerBytes, DateTimeOffset.UtcNow, null, null, OutboxMessageStatus.Pending, 0, null);

            OutboxMessageMetadata? capturedMeta1 = null;
            OutboxMessageMetadata? capturedMeta2 = null;
            int call = 0;

            _publisher.PublishRawAsync(Arg.Any<OutboxMessage>(), Arg.Any<OutboxMessageMetadata>(), Arg.Any<DispatchContext>())
                .Returns(ci =>
                {
                    if (call++ == 0) capturedMeta1 = ci.Arg<OutboxMessageMetadata>();
                    else capturedMeta2 = ci.Arg<OutboxMessageMetadata>();
                    return DispatchResult.Ok();
                });

            await _channel.WriteAsync(msg1, CancellationToken.None);
            await _channel.WriteAsync(msg2, CancellationToken.None);
            _channel.Complete();

            await _channel.ProcessMessagesAsync(CancellationToken.None);

            capturedMeta1.Should().NotBeNull();
            capturedMeta2.Should().NotBeNull();
            capturedMeta1!.Value.Entries.Length.Should().Be(1);
            capturedMeta2!.Value.Entries.Length.Should().Be(1);
            capturedMeta1.Value.Entries.Span[0].Key.Should().Be("k1");
            capturedMeta2.Value.Entries.Span[0].Key.Should().Be("k1");
        }

        [Fact]
        public async Task ProcessMessagesAsync_TraceParentAndTraceState_ExtractedFromHeaders()
        {
            var headerBytes = System.Text.Encoding.UTF8.GetBytes("{\"traceparent\":\"00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01\",\"tracestate\":\"rojo=1\"}");
            var msg = new OutboxMessage(Guid.NewGuid(), "Type", ReadOnlyMemory<byte>.Empty, null, null, headerBytes, DateTimeOffset.UtcNow, null, null, OutboxMessageStatus.Pending, 0, null);

            _publisher.PublishRawAsync(Arg.Any<OutboxMessage>(), Arg.Any<OutboxMessageMetadata>(), Arg.Any<DispatchContext>())
                .Returns(DispatchResult.Ok());

            await _channel.WriteAsync(msg, CancellationToken.None);
            _channel.Complete();

            await _channel.ProcessMessagesAsync(CancellationToken.None);

            await _publisher.Received(1).PublishRawAsync(Arg.Any<OutboxMessage>(), Arg.Any<OutboxMessageMetadata>(), Arg.Any<DispatchContext>());
        }

        [Fact]
        public async Task ProcessMessagesAsync_CustomRuntimeOptions_Respected()
        {
            var runtimeOptions = Options.Create(new OutboxRuntimeOptions { MaxPayloadSizeInBytes = 50 });
            var channel = new OutboxChannel(
                NullLogger<OutboxChannel>.Instance,
                _publisher,
                Options.Create(new OutboxDispatcherOptions()),
                runtimeOptions,
                new OutboxMetrics(Substitute.For<System.Diagnostics.Metrics.IMeterFactory>()),
                _scopeFactory,
                new DefaultErrorSanitizer(), TimeProvider.System
            );

            var payload = new byte[60];
            var msg = new OutboxMessage(Guid.NewGuid(), "Type", payload, null, null, ReadOnlyMemory<byte>.Empty, DateTimeOffset.UtcNow, null, null, OutboxMessageStatus.Pending, 0, null);

            await channel.WriteAsync(msg, CancellationToken.None);
            channel.Complete();

            await channel.ProcessMessagesAsync(CancellationToken.None);

            await _repository.Received(1).MarkAsFailedAsync(msg, Arg.Is<string>(s => s.Contains("Payload size")), true, Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task ProcessMessagesAsync_HeadersParsing_HandlesNullValuesAndReusesCache()
        {
            var headers1 = System.Text.Encoding.UTF8.GetBytes("{\"k1\":\"v1\",\"k2\":null,\"k3\":\"v3\"}");
            var headers2 = System.Text.Encoding.UTF8.GetBytes("{\"k4\":\"v4\"}");

            var msg1 = new OutboxMessage(Guid.NewGuid(), "Type", ReadOnlyMemory<byte>.Empty, null, null, headers1, DateTimeOffset.UtcNow, null, null, OutboxMessageStatus.Pending, 0, null);
            var msg2 = new OutboxMessage(Guid.NewGuid(), "Type", ReadOnlyMemory<byte>.Empty, null, null, headers2, DateTimeOffset.UtcNow, null, null, OutboxMessageStatus.Pending, 0, null);

            OutboxMessageMetadata? meta1 = null;
            OutboxMessageMetadata? meta2 = null;

            _publisher.PublishRawAsync(Arg.Is<OutboxMessage>(m => m.Id == msg1.Id), Arg.Any<OutboxMessageMetadata>(), Arg.Any<DispatchContext>())
                .Returns(call => { meta1 = call.Arg<OutboxMessageMetadata>(); return DispatchResult.Ok(); });

            _publisher.PublishRawAsync(Arg.Is<OutboxMessage>(m => m.Id == msg2.Id), Arg.Any<OutboxMessageMetadata>(), Arg.Any<DispatchContext>())
                .Returns(call => { meta2 = call.Arg<OutboxMessageMetadata>(); return DispatchResult.Ok(); });

            await _channel.WriteAsync(msg1, CancellationToken.None);
            await _channel.WriteAsync(msg2, CancellationToken.None);
            _channel.Complete();

            await _channel.ProcessMessagesAsync(CancellationToken.None);

            meta1.Should().NotBeNull();
            meta2.Should().NotBeNull();

            meta1!.Value.Entries.Length.Should().Be(2);
            meta2!.Value.Entries.Length.Should().Be(1);
        }

        [Fact]
        public void HeadersDeserializationCache_SwapAndReset_FunctionsCorrectly()
        {
            var cache = new HeadersDeserializationCache();

            var mem1 = (ReadOnlyMemory<byte>)new byte[] { 1, 2, 3 };
            var dict1 = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["a"] = "b" };

            cache.Swap(mem1, dict1);

            cache.LastHeadersMemory.Should().NotBeNull();
            cache.LastHeadersDict.Should().BeSameAs(dict1);

            cache.Reset();

            cache.LastHeadersMemory.Should().BeNull();
            cache.LastHeadersDict.Should().BeNull();
        }

        [Fact]
        public void BuildMetadata_WhenHeadersEmptyDict_EntriesIsEmpty()
        {
            var msg = new OutboxMessage(Guid.NewGuid(), "Type", ReadOnlyMemory<byte>.Empty, null, null, ReadOnlyMemory<byte>.Empty, DateTimeOffset.UtcNow, null, null, OutboxMessageStatus.Pending, 0, null);
            var emptyHeaders = new Dictionary<string, string>();
            var meta = OutboxChannel.BuildMetadata(msg, emptyHeaders);
            meta.Entries.IsEmpty.Should().BeTrue();
        }

        [Fact]
        public async Task TryDeserializeHeaders_ExactBoundary_64KB_Succeeds_And_GreaterThan64KB_Fails()
        {
            var valid64KbBytes = new byte[64 * 1024];
            valid64KbBytes[0] = (byte)'{';
            for (int i = 1; i < valid64KbBytes.Length - 1; i++) valid64KbBytes[i] = (byte)' ';
            valid64KbBytes[^1] = (byte)'}';

            var msgValid = new OutboxMessage(Guid.NewGuid(), "Type", ReadOnlyMemory<byte>.Empty, null, null, valid64KbBytes, DateTimeOffset.UtcNow, null, null, OutboxMessageStatus.Pending, 0, null);
            _publisher.PublishRawAsync(Arg.Any<OutboxMessage>(), Arg.Any<OutboxMessageMetadata>(), Arg.Any<DispatchContext>())
                .Returns(DispatchResult.Ok());

            await _channel.WriteAsync(msgValid, CancellationToken.None);
            _channel.Complete();

            await _channel.ProcessMessagesAsync(CancellationToken.None);
            await _repository.Received(1).MarkAsDispatchedAsync(Arg.Any<IReadOnlyList<OutboxMessage>>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task TryDeserializeHeaders_WithTraceParentAndTraceState_ExtractsParentTrace()
        {
            var headersJson = System.Text.Encoding.UTF8.GetBytes("{\"traceparent\":\"00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01\",\"tracestate\":\"congo=t61rcWkgMzE\"}");
            var msg = new OutboxMessage(Guid.NewGuid(), "Type", ReadOnlyMemory<byte>.Empty, null, null, headersJson, DateTimeOffset.UtcNow, null, null, OutboxMessageStatus.Pending, 0, null);

            OutboxMessageMetadata? capturedMeta = null;
            _publisher.PublishRawAsync(Arg.Any<OutboxMessage>(), Arg.Any<OutboxMessageMetadata>(), Arg.Any<DispatchContext>())
                .Returns(call => {
                    capturedMeta = call.Arg<OutboxMessageMetadata>();
                    return DispatchResult.Ok();
                });

            await _channel.WriteAsync(msg, CancellationToken.None);
            _channel.Complete();

            await _channel.ProcessMessagesAsync(CancellationToken.None);

            capturedMeta.Should().NotBeNull();
            capturedMeta!.Value.GetValue("traceparent").Should().Be("00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01");
            capturedMeta.Value.GetValue("tracestate").Should().Be("congo=t61rcWkgMzE");
        }

        [Fact]
        public async Task ParseHeadersFast_WithArrayOrScalar_DoesNotThrow()
        {
            var arrayJson = System.Text.Encoding.UTF8.GetBytes("[1,2,3]");
            var msg = new OutboxMessage(Guid.NewGuid(), "Type", ReadOnlyMemory<byte>.Empty, null, null, arrayJson, DateTimeOffset.UtcNow, null, null, OutboxMessageStatus.Pending, 0, null);

            OutboxMessageMetadata? capturedMeta = null;
            _publisher.PublishRawAsync(Arg.Any<OutboxMessage>(), Arg.Any<OutboxMessageMetadata>(), Arg.Any<DispatchContext>())
                .Returns(call => {
                    capturedMeta = call.Arg<OutboxMessageMetadata>();
                    return DispatchResult.Ok();
                });

            await _channel.WriteAsync(msg, CancellationToken.None);
            _channel.Complete();

            await _channel.ProcessMessagesAsync(CancellationToken.None);

            capturedMeta.Should().NotBeNull();
            capturedMeta!.Value.Entries.IsEmpty.Should().BeTrue();
        }

        [Fact]
        public void HeadersDeserializationCache_SwapThreeTimes_ReusesLastHeadersDict()
        {
            var cache = new HeadersDeserializationCache();

            var mem1 = (ReadOnlyMemory<byte>)new byte[] { 1 };
            var dict1 = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["1"] = "1" };
            var mem2 = (ReadOnlyMemory<byte>)new byte[] { 2 };
            var dict2 = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["2"] = "2" };
            var mem3 = (ReadOnlyMemory<byte>)new byte[] { 3 };
            var dict3 = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["3"] = "3" };

            cache.Swap(mem1, dict1);
            cache.CurrentHeaders.Should().NotBeNull();

            cache.Swap(mem2, dict2);
            cache.CurrentHeaders.Should().BeSameAs(dict1);

            cache.Swap(mem3, dict3);
            cache.CurrentHeaders.Should().BeSameAs(dict2);

            cache.LastHeadersDict.Should().BeSameAs(dict3);
        }

        [Fact]
        public async Task ParseHeadersFast_WithVariousJsonShapes_HandlesAccurately()
        {
            var json1 = System.Text.Encoding.UTF8.GetBytes("{\"k1\":\"v1\",\"k2\":null,\"k3\":\"v3\"}");
            var msg1 = new OutboxMessage(Guid.NewGuid(), "Type", ReadOnlyMemory<byte>.Empty, null, null, json1, DateTimeOffset.UtcNow, null, null, OutboxMessageStatus.Pending, 0, null);

            var json2 = System.Text.Encoding.UTF8.GetBytes("{}");
            var msg2 = new OutboxMessage(Guid.NewGuid(), "Type", ReadOnlyMemory<byte>.Empty, null, null, json2, DateTimeOffset.UtcNow, null, null, OutboxMessageStatus.Pending, 0, null);

            var json3 = System.Text.Encoding.UTF8.GetBytes("[1,2,3]");
            var msg3 = new OutboxMessage(Guid.NewGuid(), "Type", ReadOnlyMemory<byte>.Empty, null, null, json3, DateTimeOffset.UtcNow, null, null, OutboxMessageStatus.Pending, 0, null);

            var metas = new List<OutboxMessageMetadata>();
            _publisher.PublishRawAsync(Arg.Any<OutboxMessage>(), Arg.Any<OutboxMessageMetadata>(), Arg.Any<DispatchContext>())
                .Returns(call => {
                    metas.Add(call.Arg<OutboxMessageMetadata>());
                    return DispatchResult.Ok();
                });

            await _channel.WriteAsync(msg1, CancellationToken.None);
            await _channel.WriteAsync(msg2, CancellationToken.None);
            await _channel.WriteAsync(msg3, CancellationToken.None);
            _channel.Complete();

            await _channel.ProcessMessagesAsync(CancellationToken.None);

            metas.Should().HaveCount(3);
            metas[0].GetValue("k1").Should().Be("v1");
            metas[0].GetValue("k2").Should().BeNull();
            metas[0].GetValue("k3").Should().Be("v3");

            metas[1].Entries.IsEmpty.Should().BeTrue();
            metas[2].Entries.IsEmpty.Should().BeTrue();
        }

        [Fact]
        public async Task ProcessMessagesAsync_ConsecutiveDifferentHeaders_IsolatesState()
        {
            var json1 = System.Text.Encoding.UTF8.GetBytes("{\"k1\":\"v1\"}");
            var msg1 = new OutboxMessage(Guid.NewGuid(), "Type", ReadOnlyMemory<byte>.Empty, null, null, json1, DateTimeOffset.UtcNow, null, null, OutboxMessageStatus.Pending, 0, null);

            var json2 = System.Text.Encoding.UTF8.GetBytes("{\"k2\":\"v2\"}");
            var msg2 = new OutboxMessage(Guid.NewGuid(), "Type", ReadOnlyMemory<byte>.Empty, null, null, json2, DateTimeOffset.UtcNow, null, null, OutboxMessageStatus.Pending, 0, null);

            var json3 = System.Text.Encoding.UTF8.GetBytes("{\"k3\":\"v3\"}");
            var msg3 = new OutboxMessage(Guid.NewGuid(), "Type", ReadOnlyMemory<byte>.Empty, null, null, json3, DateTimeOffset.UtcNow, null, null, OutboxMessageStatus.Pending, 0, null);

            var received = new List<OutboxMessageMetadata>();
            _publisher.PublishRawAsync(Arg.Any<OutboxMessage>(), Arg.Any<OutboxMessageMetadata>(), Arg.Any<DispatchContext>())
                .Returns(call => {
                    received.Add(call.Arg<OutboxMessageMetadata>());
                    return DispatchResult.Ok();
                });

            await _channel.WriteAsync(msg1, CancellationToken.None);
            await _channel.WriteAsync(msg2, CancellationToken.None);
            await _channel.WriteAsync(msg3, CancellationToken.None);
            _channel.Complete();

            await _channel.ProcessMessagesAsync(CancellationToken.None);

            received.Should().HaveCount(3);
            received[0].GetValue("k1").Should().Be("v1");
            received[0].GetValue("k2").Should().BeNull();
            received[0].GetValue("k3").Should().BeNull();

            received[1].GetValue("k1").Should().BeNull();
            received[1].GetValue("k2").Should().Be("v2");
            received[1].GetValue("k3").Should().BeNull();

            received[2].GetValue("k1").Should().BeNull();
            received[2].GetValue("k2").Should().BeNull();
            received[2].GetValue("k3").Should().Be("v3");

            await _repository.Received(1).MarkAsDispatchedAsync(Arg.Is<IReadOnlyList<OutboxMessage>>(l => l.Count == 3), Arg.Any<CancellationToken>());
        }

        [Fact]
        public void BuildMetadata_WhenHeadersDictionaryIsEmpty_EntriesIsEmpty()
        {
            var msg = new OutboxMessage(Guid.NewGuid(), "Type", ReadOnlyMemory<byte>.Empty, "corr-1", "caus-1", ReadOnlyMemory<byte>.Empty, DateTimeOffset.UtcNow, null, null, OutboxMessageStatus.Pending, 0, null);
            var metadata = OutboxChannel.BuildMetadata(msg, new Dictionary<string, string>());
            metadata.Entries.IsEmpty.Should().BeTrue();
            var objField = typeof(ReadOnlyMemory<MetadataEntry>).GetField("_object", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var backingObject = objField?.GetValue(metadata.Entries);
            backingObject.Should().BeNull();
            metadata.CorrelationId.Should().Be("corr-1");
            metadata.CausationId.Should().Be("caus-1");
            metadata.MessageType.Should().Be("Type");
        }

        [Theory]
        [InlineData(1, 0.0, 750)]   // 1000 * 1 * (1 - 0.25) = 750ms
        [InlineData(1, 0.5, 1000)]  // 1000 * 1 * (1 + 0.0) = 1000ms
        [InlineData(1, 1.0, 1250)]  // 1000 * 1 * (1 + 0.25) = 1250ms
        [InlineData(2, 0.5, 2000)]  // 1000 * 2 = 2000ms
        [InlineData(3, 0.5, 4000)]  // 1000 * 4 = 4000ms
        [InlineData(15, 0.5, 1024000)] // 1000 * 2^10 = 1024000ms (clamped at 10)
        public void CalculateBackoffDelay_WithKnownRandomValues_ReturnsExpectedJitteredDelay(int attempt, double rand, int expectedMs)
        {
            var delay = OutboxChannel.CalculateBackoffDelay(attempt, 1000, () => rand);
            delay.TotalMilliseconds.Should().Be(expectedMs);
        }

        [Fact]
        public void CalculateBackoffDelay_WhenZeroOrNegative_ClampsToAtLeastOneMillisecond()
        {
            var delay = OutboxChannel.CalculateBackoffDelay(1, 0, () => 0.0);
            delay.TotalMilliseconds.Should().Be(1);

            var delay2 = OutboxChannel.CalculateBackoffDelay(1, 100);
            delay2.TotalMilliseconds.Should().BeInRange(75, 125);
        }

        [Theory]
        [InlineData("123")]
        [InlineData("\"string\"")]
        [InlineData("{}")]
        public async Task ProcessMessagesAsync_WhenHeadersNotJsonObjectOrEmpty_HandlesGracefullyWithoutThrowing(string jsonHeaders)
        {
            var headersBytes = System.Text.Encoding.UTF8.GetBytes(jsonHeaders);
            var msg = new OutboxMessage(Guid.NewGuid(), "Type", ReadOnlyMemory<byte>.Empty, null, null, headersBytes, DateTimeOffset.UtcNow, null, null, OutboxMessageStatus.Pending, 0, null);

            _publisher.PublishRawAsync(Arg.Any<OutboxMessage>(), Arg.Any<OutboxMessageMetadata>(), Arg.Any<DispatchContext>())
                .Returns(DispatchResult.Ok());

            await _channel.WriteAsync(msg, CancellationToken.None);
            _channel.Complete();

            await _channel.ProcessMessagesAsync(CancellationToken.None);

            await _repository.Received(1).MarkAsDispatchedAsync(Arg.Is<IReadOnlyList<OutboxMessage>>(l => l.Count == 1), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task ProcessMessagesAsync_MultipleBatches_ResetsHeadersCacheAcrossBatches()
        {
            var msg1 = new OutboxMessage(Guid.NewGuid(), "Type", ReadOnlyMemory<byte>.Empty, null, null, System.Text.Encoding.UTF8.GetBytes("{\"k\":\"batch1\"}"), DateTimeOffset.UtcNow, null, null, OutboxMessageStatus.Pending, 0, null);
            var msg2 = new OutboxMessage(Guid.NewGuid(), "Type", ReadOnlyMemory<byte>.Empty, null, null, System.Text.Encoding.UTF8.GetBytes("{\"k\":\"batch2\"}"), DateTimeOffset.UtcNow, null, null, OutboxMessageStatus.Pending, 0, null);

            var captured = new List<string?>();
            _publisher.PublishRawAsync(Arg.Any<OutboxMessage>(), Arg.Any<OutboxMessageMetadata>(), Arg.Any<DispatchContext>())
                .Returns(call => {
                    lock (captured)
                    {
                        captured.Add(call.Arg<OutboxMessageMetadata>().GetValue("k"));
                    }
                    return DispatchResult.Ok();
                });

            // Write msg1, start processing
            await _channel.WriteAsync(msg1, CancellationToken.None);
            var processTask = _channel.ProcessMessagesAsync(CancellationToken.None);

            // Wait until msg1 is processed
            for (int i = 0; i < 50; i++)
            {
                lock (captured)
                {
                    if (captured.Count >= 1) break;
                }
                await Task.Delay(10);
            }

            // Write msg2, complete channel
            await _channel.WriteAsync(msg2, CancellationToken.None);
            _channel.Complete();

            await processTask;

            captured.Should().Equal("batch1", "batch2");
        }
    }
}

