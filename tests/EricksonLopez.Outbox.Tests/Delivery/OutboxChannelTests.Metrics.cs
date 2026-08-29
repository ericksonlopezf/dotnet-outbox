// Copyright © Erickson Lopez. MIT License.
#pragma warning disable CA2012, CS8600, CS8602
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using EricksonLopez.Outbox;
using EricksonLopez.Outbox.Diagnostics;
using EricksonLopez.Outbox.Dispatcher;
using EricksonLopez.Outbox.Persistence;
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
    public class MetricsAndTelemetryTests
    {
        private readonly IBrokerPublisher _publisher = Substitute.For<IBrokerPublisher>();
        private readonly IOutboxRepository _repository = Substitute.For<IOutboxRepository>();
        private readonly IDeadLetterRepository _dlqRepository = Substitute.For<IDeadLetterRepository>();
        private readonly IServiceScopeFactory _scopeFactory = Substitute.For<IServiceScopeFactory>();
        private readonly IServiceScope _scope = Substitute.For<IServiceScope>();
        private readonly IServiceProvider _serviceProvider = Substitute.For<IServiceProvider>();

        public MetricsAndTelemetryTests()
        {
            _scopeFactory.CreateScope().Returns(_scope);
            _scope.ServiceProvider.Returns(_serviceProvider);
            _serviceProvider.GetService(typeof(IOutboxRepository)).Returns(_repository);
            _serviceProvider.GetService(typeof(IDeadLetterRepository)).Returns(_dlqRepository);
            _serviceProvider.GetService(typeof(IEnumerable<Pipeline.IOutboxMiddleware>)).Returns(Array.Empty<Pipeline.IOutboxMiddleware>());
        }

        [Fact]
        public async Task ProcessMessagesAsync_WhenIncludeMessageTypeTagTrue_RecordsTag()
        {
            var metrics = new OutboxMetrics();
            string? recordedTag = null;
            using var meterListener = new System.Diagnostics.Metrics.MeterListener();
            meterListener.InstrumentPublished = (inst, listener) =>
            {
                if (inst.Meter == metrics.Meter && inst.Name == "messaging.outbox.messages.dispatched")
                {
                    listener.EnableMeasurementEvents(inst);
                }
            };
            meterListener.SetMeasurementEventCallback<long>((inst, measurement, tags, state) =>
            {
                if (inst.Meter == metrics.Meter)
                {
                    foreach (var t in tags)
                    {
                        if (t.Key == "message_type") recordedTag = t.Value?.ToString();
                    }
                }
            });
            meterListener.Start();

            var channel = new OutboxChannel(
                NullLogger<OutboxChannel>.Instance,
                _publisher,
                Options.Create(new OutboxDispatcherOptions()),
                Options.Create(new OutboxRuntimeOptions { IncludeMessageTypeTag = true }),
                metrics,
                _scopeFactory,
                new DefaultErrorSanitizer(), TimeProvider.System
            );

            var msg = new OutboxMessage(Guid.NewGuid(), "OrderShipped", ReadOnlyMemory<byte>.Empty, null, null, ReadOnlyMemory<byte>.Empty, DateTimeOffset.UtcNow, null, null, OutboxMessageStatus.Pending, 0, null);
            _publisher.PublishRawAsync(Arg.Any<OutboxMessage>(), Arg.Any<OutboxMessageMetadata>(), Arg.Any<DispatchContext>())
                .Returns(DispatchResult.Ok());

            await channel.WriteAsync(msg, CancellationToken.None);
            channel.Complete();

            await channel.ProcessMessagesAsync(CancellationToken.None);

            recordedTag.Should().Be("OrderShipped");
        }

        [Fact]
        public async Task ProcessMessagesAsync_WhenIncludeMessageTypeTagFalse_DoesNotRecordTag()
        {
            var metrics = new OutboxMetrics();
            bool hasTag = false;
            using var meterListener = new System.Diagnostics.Metrics.MeterListener();
            meterListener.InstrumentPublished = (inst, listener) =>
            {
                if (inst.Meter == metrics.Meter && inst.Name == "messaging.outbox.messages.dispatched")
                {
                    listener.EnableMeasurementEvents(inst);
                }
            };
            meterListener.SetMeasurementEventCallback<long>((inst, measurement, tags, state) =>
            {
                if (inst.Meter == metrics.Meter)
                {
                    foreach (var t in tags)
                    {
                        if (t.Key == "message_type") hasTag = true;
                    }
                }
            });
            meterListener.Start();

            var channel = new OutboxChannel(
                NullLogger<OutboxChannel>.Instance,
                _publisher,
                Options.Create(new OutboxDispatcherOptions()),
                Options.Create(new OutboxRuntimeOptions { IncludeMessageTypeTag = false }),
                metrics,
                _scopeFactory,
                new DefaultErrorSanitizer(), TimeProvider.System
            );

            var msg = new OutboxMessage(Guid.NewGuid(), "OrderShipped", ReadOnlyMemory<byte>.Empty, null, null, ReadOnlyMemory<byte>.Empty, DateTimeOffset.UtcNow, null, null, OutboxMessageStatus.Pending, 0, null);
            _publisher.PublishRawAsync(Arg.Any<OutboxMessage>(), Arg.Any<OutboxMessageMetadata>(), Arg.Any<DispatchContext>())
                .Returns(DispatchResult.Ok());

            await channel.WriteAsync(msg, CancellationToken.None);
            channel.Complete();

            await channel.ProcessMessagesAsync(CancellationToken.None);

            hasTag.Should().BeFalse();
        }

        [Fact]
        public async Task ProcessMessagesAsync_FailureMetrics_TransientAndFatalTags()
        {
            var metrics = new OutboxMetrics();
            var recordedErrors = new List<string>();
            using var meterListener = new System.Diagnostics.Metrics.MeterListener();
            meterListener.InstrumentPublished = (inst, listener) =>
            {
                if (inst.Meter == metrics.Meter && inst.Name == "messaging.outbox.dispatch.errors")
                {
                    listener.EnableMeasurementEvents(inst);
                }
            };
            meterListener.SetMeasurementEventCallback<long>((inst, measurement, tags, state) =>
            {
                if (inst.Meter == metrics.Meter)
                {
                    foreach (var t in tags)
                    {
                        if (t.Key == "error.type") recordedErrors.Add(t.Value?.ToString() ?? "");
                    }
                }
            });
            meterListener.Start();

            var channel = new OutboxChannel(
                NullLogger<OutboxChannel>.Instance,
                _publisher,
                Options.Create(new OutboxDispatcherOptions()),
                Options.Create(new OutboxRuntimeOptions { IncludeMessageTypeTag = true }),
                metrics,
                _scopeFactory,
                new DefaultErrorSanitizer(), TimeProvider.System
            );

            var msg1 = new OutboxMessage(Guid.NewGuid(), "Type1", ReadOnlyMemory<byte>.Empty, null, null, ReadOnlyMemory<byte>.Empty, DateTimeOffset.UtcNow, null, null, OutboxMessageStatus.Pending, 0, null);
            var msg2 = new OutboxMessage(Guid.NewGuid(), "Type2", ReadOnlyMemory<byte>.Empty, null, null, ReadOnlyMemory<byte>.Empty, DateTimeOffset.UtcNow, null, null, OutboxMessageStatus.Pending, 0, null);

            _publisher.PublishRawAsync(Arg.Is<OutboxMessage>(m => m.Id == msg1.Id), Arg.Any<OutboxMessageMetadata>(), Arg.Any<DispatchContext>())
                .Returns(DispatchResult.FailAndRetry(new InvalidOperationException("Transient")));
            _publisher.PublishRawAsync(Arg.Is<OutboxMessage>(m => m.Id == msg2.Id), Arg.Any<OutboxMessageMetadata>(), Arg.Any<DispatchContext>())
                .Returns(DispatchResult.FailFatal(new InvalidOperationException("Fatal")));

            await channel.WriteAsync(msg1, CancellationToken.None);
            await channel.WriteAsync(msg2, CancellationToken.None);
            channel.Complete();

            await channel.ProcessMessagesAsync(CancellationToken.None);

            recordedErrors.Should().Contain("transient");
            recordedErrors.Should().Contain("fatal");
        }

        [Fact]
        public async Task ProcessMessagesAsync_FailureMetrics_WithoutMessageTypeTag()
        {
            var metrics = new OutboxMetrics();
            var recordedErrors = new List<string>();
            using var meterListener = new System.Diagnostics.Metrics.MeterListener();
            meterListener.InstrumentPublished = (inst, listener) =>
            {
                if (inst.Meter == metrics.Meter && inst.Name == "messaging.outbox.dispatch.errors")
                {
                    listener.EnableMeasurementEvents(inst);
                }
            };
            meterListener.SetMeasurementEventCallback<long>((inst, measurement, tags, state) =>
            {
                if (inst.Meter == metrics.Meter)
                {
                    foreach (var t in tags)
                    {
                        if (t.Key == "error.type") recordedErrors.Add(t.Value?.ToString() ?? "");
                    }
                }
            });
            meterListener.Start();

            var channel = new OutboxChannel(
                NullLogger<OutboxChannel>.Instance,
                _publisher,
                Options.Create(new OutboxDispatcherOptions()),
                Options.Create(new OutboxRuntimeOptions { IncludeMessageTypeTag = false }),
                metrics,
                _scopeFactory,
                new DefaultErrorSanitizer(), TimeProvider.System
            );

            var msg1 = new OutboxMessage(Guid.NewGuid(), "Type1", ReadOnlyMemory<byte>.Empty, null, null, ReadOnlyMemory<byte>.Empty, DateTimeOffset.UtcNow, null, null, OutboxMessageStatus.Pending, 0, null);
            var msg2 = new OutboxMessage(Guid.NewGuid(), "Type2", ReadOnlyMemory<byte>.Empty, null, null, ReadOnlyMemory<byte>.Empty, DateTimeOffset.UtcNow, null, null, OutboxMessageStatus.Pending, 0, null);

            _publisher.PublishRawAsync(Arg.Is<OutboxMessage>(m => m.Id == msg1.Id), Arg.Any<OutboxMessageMetadata>(), Arg.Any<DispatchContext>())
                .Returns(DispatchResult.FailAndRetry(new InvalidOperationException("Transient")));
            _publisher.PublishRawAsync(Arg.Is<OutboxMessage>(m => m.Id == msg2.Id), Arg.Any<OutboxMessageMetadata>(), Arg.Any<DispatchContext>())
                .Returns(DispatchResult.FailFatal(new InvalidOperationException("Fatal")));

            await channel.WriteAsync(msg1, CancellationToken.None);
            await channel.WriteAsync(msg2, CancellationToken.None);
            channel.Complete();

            await channel.ProcessMessagesAsync(CancellationToken.None);

            recordedErrors.Should().Contain("transient");
            recordedErrors.Should().Contain("fatal");
        }

        [Fact]
        public async Task ProcessMessagesAsync_WhenHeadersContainTraceparent_PropagatesParentTraceToActivity()
        {
            var uniqueMessageType = $"TraceparentTest_{Guid.NewGuid():N}";
            var expectedTraceId = "4bf92f3577b34da6a3ce929d0e0e4736";
            var expectedSpanId = "00f067aa0ba902b7";
            var traceparent = $"00-{expectedTraceId}-{expectedSpanId}-01";
            var headersJson = System.Text.Encoding.UTF8.GetBytes($"{{\"traceparent\":\"{traceparent}\"}}");

            string? capturedParentId = null;
            using var listener = new System.Diagnostics.ActivityListener
            {
                ShouldListenTo = s => s.Name == "EricksonLopez.Outbox",
                Sample = (ref System.Diagnostics.ActivityCreationOptions<System.Diagnostics.ActivityContext> options) => System.Diagnostics.ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = a =>
                {
                    if (a.GetTagItem("messaging.destination.name")?.ToString() == uniqueMessageType)
                    {
                        capturedParentId = a.ParentId ?? $"00-{a.TraceId}-{a.ParentSpanId}-01";
                    }
                }
            };
            System.Diagnostics.ActivitySource.AddActivityListener(listener);

            var channel = new OutboxChannel(
                NullLogger<OutboxChannel>.Instance,
                _publisher,
                Options.Create(new OutboxDispatcherOptions()),
                Options.Create(new OutboxRuntimeOptions()),
                new OutboxMetrics(),
                _scopeFactory,
                new DefaultErrorSanitizer(),
                TimeProvider.System
            );

            var msg = new OutboxMessage(Guid.NewGuid(), uniqueMessageType, ReadOnlyMemory<byte>.Empty, null, null, headersJson, DateTimeOffset.UtcNow, null, null, OutboxMessageStatus.Pending, 0, null);
            _publisher.PublishRawAsync(Arg.Any<OutboxMessage>(), Arg.Any<OutboxMessageMetadata>(), Arg.Any<DispatchContext>())
                .Returns(DispatchResult.Ok());

            await channel.WriteAsync(msg, CancellationToken.None);
            channel.Complete();

            await channel.ProcessMessagesAsync(CancellationToken.None);

            capturedParentId.Should().Be(traceparent);
        }

        [Fact]
        public async Task ProcessMessagesAsync_WhenSkipExecutionTrue_DoesNotStartActivity()
        {
            var uniqueMessageType = $"SkipPayloadTest_{Guid.NewGuid():N}";
            int activityStarts = 0;
            using var listener = new System.Diagnostics.ActivityListener
            {
                ShouldListenTo = s => s.Name == "EricksonLopez.Outbox",
                Sample = (ref System.Diagnostics.ActivityCreationOptions<System.Diagnostics.ActivityContext> options) => System.Diagnostics.ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = a =>
                {
                    if (a.OperationName == "Outbox.Dispatch" && a.GetTagItem("messaging.destination.name")?.ToString() == uniqueMessageType)
                    {
                        Interlocked.Increment(ref activityStarts);
                    }
                }
            };
            System.Diagnostics.ActivitySource.AddActivityListener(listener);

            var runtimeOptions = new OutboxRuntimeOptions { MaxPayloadSizeInBytes = 5 };
            var channel = new OutboxChannel(
                NullLogger<OutboxChannel>.Instance,
                _publisher,
                Options.Create(new OutboxDispatcherOptions()),
                Options.Create(runtimeOptions),
                new OutboxMetrics(),
                _scopeFactory,
                new DefaultErrorSanitizer(),
                TimeProvider.System
            );

            // Message with payload larger than max (10 > 5) -> skipExecution = true
            var msg = new OutboxMessage(Guid.NewGuid(), uniqueMessageType, new byte[10], null, null, ReadOnlyMemory<byte>.Empty, DateTimeOffset.UtcNow, null, null, OutboxMessageStatus.Pending, 0, null);

            await channel.WriteAsync(msg, CancellationToken.None);
            channel.Complete();

            await channel.ProcessMessagesAsync(CancellationToken.None);

            activityStarts.Should().Be(0);
        }

        [Fact]
        public async Task ProcessMessagesAsync_RecordsDispatchDuration_PositiveOnSuccess_AndZeroOnSkipExecution()
        {
            var metrics = new OutboxMetrics();
            var recordedDurations = new List<double>();

            using var meterListener = new System.Diagnostics.Metrics.MeterListener();
            meterListener.InstrumentPublished = (inst, listener) =>
            {
                if (inst.Meter == metrics.Meter && inst.Name == "messaging.outbox.publish.duration")
                {
                    listener.EnableMeasurementEvents(inst);
                }
            };
            meterListener.SetMeasurementEventCallback<double>((inst, measurement, tags, state) =>
            {
                if (inst.Meter == metrics.Meter && inst.Name == "messaging.outbox.publish.duration")
                {
                    recordedDurations.Add(measurement);
                }
            });
            meterListener.Start();

            var runtimeOptions = new OutboxRuntimeOptions { MaxPayloadSizeInBytes = 10 };
            var channel = new OutboxChannel(
                NullLogger<OutboxChannel>.Instance,
                _publisher,
                Options.Create(new OutboxDispatcherOptions()),
                Options.Create(runtimeOptions),
                metrics,
                _scopeFactory,
                new DefaultErrorSanitizer(),
                TimeProvider.System
            );

            // Message 1: valid -> skipExecution = false
            var msg1 = new OutboxMessage(Guid.NewGuid(), "OrderCreated", new byte[5], null, null, ReadOnlyMemory<byte>.Empty, DateTimeOffset.UtcNow, null, null, OutboxMessageStatus.Pending, 0, null);
            _publisher.PublishRawAsync(Arg.Any<OutboxMessage>(), Arg.Any<OutboxMessageMetadata>(), Arg.Any<DispatchContext>())
                .Returns(DispatchResult.Ok());

            // Message 2: payload too large (20 > 10) -> skipExecution = true
            var msg2 = new OutboxMessage(Guid.NewGuid(), "OrderCreated", new byte[20], null, null, ReadOnlyMemory<byte>.Empty, DateTimeOffset.UtcNow, null, null, OutboxMessageStatus.Pending, 0, null);

            await channel.WriteAsync(msg1, CancellationToken.None);
            await channel.WriteAsync(msg2, CancellationToken.None);
            channel.Complete();

            await channel.ProcessMessagesAsync(CancellationToken.None);

            recordedDurations.Should().HaveCount(2);
            recordedDurations[0].Should().BeInRange(0.0000001, 100.0);
            recordedDurations[1].Should().Be(0.0);
        }

        [Fact]
        public async Task ProcessMessagesAsync_WhenIncludeMessageTypeTagFalse_OmitsMessageTypeTagOnFailure()
        {
            var metrics = new OutboxMetrics();
            var tagNames = new List<string>();

            using var meterListener = new System.Diagnostics.Metrics.MeterListener();
            meterListener.InstrumentPublished = (inst, listener) =>
            {
                if (inst.Meter == metrics.Meter && inst.Name == "messaging.outbox.dispatch.errors")
                {
                    listener.EnableMeasurementEvents(inst);
                }
            };
            meterListener.SetMeasurementEventCallback<long>((inst, measurement, tags, state) =>
            {
                if (inst.Meter == metrics.Meter && inst.Name == "messaging.outbox.dispatch.errors")
                {
                    foreach (var tag in tags)
                        tagNames.Add(tag.Key);
                }
            });
            meterListener.Start();

            var channel = new OutboxChannel(
                NullLogger<OutboxChannel>.Instance,
                _publisher,
                Options.Create(new OutboxDispatcherOptions()),
                Options.Create(new OutboxRuntimeOptions { IncludeMessageTypeTag = false }),
                metrics,
                _scopeFactory,
                new DefaultErrorSanitizer(),
                TimeProvider.System
            );

            var msg = new OutboxMessage(Guid.NewGuid(), "OrderCreated", ReadOnlyMemory<byte>.Empty, null, null, ReadOnlyMemory<byte>.Empty, DateTimeOffset.UtcNow, null, null, OutboxMessageStatus.Pending, 0, null);
            _publisher.PublishRawAsync(Arg.Any<OutboxMessage>(), Arg.Any<OutboxMessageMetadata>(), Arg.Any<DispatchContext>())
                .Returns(DispatchResult.FailFatal(new InvalidOperationException("Fatal error")));

            await channel.WriteAsync(msg, CancellationToken.None);
            channel.Complete();

            await channel.ProcessMessagesAsync(CancellationToken.None);

            tagNames.Should().Contain("error.type");
            tagNames.Should().NotContain("message_type");
        }

        [Fact]
        public async Task ProcessMessagesAsync_WhenRetry_IncrementsRetryAttemptsTotal()
        {
            var metrics = new OutboxMetrics();
            long retryCount = 0;

            using var meterListener = new System.Diagnostics.Metrics.MeterListener();
            meterListener.InstrumentPublished = (inst, listener) =>
            {
                if (inst.Meter == metrics.Meter && inst.Name == "messaging.outbox.retry.attempts")
                {
                    listener.EnableMeasurementEvents(inst);
                }
            };
            meterListener.SetMeasurementEventCallback<long>((inst, measurement, tags, state) =>
            {
                if (inst.Meter == metrics.Meter && inst.Name == "messaging.outbox.retry.attempts")
                {
                    Interlocked.Add(ref retryCount, measurement);
                }
            });
            meterListener.Start();

            var channel = new OutboxChannel(
                NullLogger<OutboxChannel>.Instance,
                _publisher,
                Options.Create(new OutboxDispatcherOptions()),
                Options.Create(new OutboxRuntimeOptions()),
                metrics,
                _scopeFactory,
                new DefaultErrorSanitizer(),
                TimeProvider.System
            );

            var msg = new OutboxMessage(Guid.NewGuid(), "OrderCreated", ReadOnlyMemory<byte>.Empty, null, null, ReadOnlyMemory<byte>.Empty, DateTimeOffset.UtcNow, null, null, OutboxMessageStatus.Pending, 0, null);
            _publisher.PublishRawAsync(Arg.Any<OutboxMessage>(), Arg.Any<OutboxMessageMetadata>(), Arg.Any<DispatchContext>())
                .Returns(DispatchResult.FailAndRetry(new InvalidOperationException("Transient error")));

            await channel.WriteAsync(msg, CancellationToken.None);
            channel.Complete();

            await channel.ProcessMessagesAsync(CancellationToken.None);

            retryCount.Should().Be(1);
        }

        [Fact]
        public async Task ProcessMessagesAsync_WhenDeadLetter_IncrementsDeadLettersTotal()
        {
            var metrics = new OutboxMetrics();
            long dlqCount = 0;

            using var meterListener = new System.Diagnostics.Metrics.MeterListener();
            meterListener.InstrumentPublished = (inst, listener) =>
            {
                if (inst.Meter == metrics.Meter && inst.Name == "messaging.outbox.messages.dead_lettered")
                {
                    listener.EnableMeasurementEvents(inst);
                }
            };
            meterListener.SetMeasurementEventCallback<long>((inst, measurement, tags, state) =>
            {
                if (inst.Meter == metrics.Meter && inst.Name == "messaging.outbox.messages.dead_lettered")
                {
                    Interlocked.Add(ref dlqCount, measurement);
                }
            });
            meterListener.Start();

            var channel = new OutboxChannel(
                NullLogger<OutboxChannel>.Instance,
                _publisher,
                Options.Create(new OutboxDispatcherOptions()),
                Options.Create(new OutboxRuntimeOptions()),
                metrics,
                _scopeFactory,
                new DefaultErrorSanitizer(),
                TimeProvider.System
            );

            var msg = new OutboxMessage(Guid.NewGuid(), "OrderCreated", ReadOnlyMemory<byte>.Empty, null, null, ReadOnlyMemory<byte>.Empty, DateTimeOffset.UtcNow, null, null, OutboxMessageStatus.Pending, 10, null);
            _publisher.PublishRawAsync(Arg.Any<OutboxMessage>(), Arg.Any<OutboxMessageMetadata>(), Arg.Any<DispatchContext>())
                .Returns(DispatchResult.FailFatal(new InvalidOperationException("Fatal error")));

            await channel.WriteAsync(msg, CancellationToken.None);
            channel.Complete();

            await channel.ProcessMessagesAsync(CancellationToken.None);

            dlqCount.Should().Be(1);
        }

        [Fact]
        public async Task ProcessMessagesAsync_WhenDlqInsertFails_IncrementsDlqInsertFailuresWithMessageTypeTag()
        {
            var metrics = new OutboxMetrics();
            long dlqInsertFailures = 0;
            string? capturedMessageType = null;

            using var meterListener = new System.Diagnostics.Metrics.MeterListener();
            meterListener.InstrumentPublished = (inst, listener) =>
            {
                if (inst.Meter == metrics.Meter && inst.Name == "messaging.outbox.dlq.insert_failures")
                {
                    listener.EnableMeasurementEvents(inst);
                }
            };
            meterListener.SetMeasurementEventCallback<long>((inst, measurement, tags, state) =>
            {
                if (inst.Meter == metrics.Meter && inst.Name == "messaging.outbox.dlq.insert_failures")
                {
                    Interlocked.Add(ref dlqInsertFailures, measurement);
                    foreach (var tag in tags)
                    {
                        if (tag.Key == "message_type")
                            capturedMessageType = tag.Value?.ToString();
                    }
                }
            });
            meterListener.Start();

            var channel = new OutboxChannel(
                NullLogger<OutboxChannel>.Instance,
                _publisher,
                Options.Create(new OutboxDispatcherOptions()),
                Options.Create(new OutboxRuntimeOptions()),
                metrics,
                _scopeFactory,
                new DefaultErrorSanitizer(),
                TimeProvider.System
            );

            var msg = new OutboxMessage(Guid.NewGuid(), "OrderCreated", ReadOnlyMemory<byte>.Empty, null, null, ReadOnlyMemory<byte>.Empty, DateTimeOffset.UtcNow, null, null, OutboxMessageStatus.Pending, 10, null);
            _publisher.PublishRawAsync(Arg.Any<OutboxMessage>(), Arg.Any<OutboxMessageMetadata>(), Arg.Any<DispatchContext>())
                .Returns(DispatchResult.FailFatal(new InvalidOperationException("Fatal error")));

            _dlqRepository.InsertAsync(Arg.Any<DeadLetterMessage>(), Arg.Any<IOutboxTransactionContext?>(), Arg.Any<CancellationToken>())
                .Returns(ValueTask.FromException(new InvalidOperationException("DLQ DB error")));

            await channel.WriteAsync(msg, CancellationToken.None);
            channel.Complete();

            await channel.ProcessMessagesAsync(CancellationToken.None);

            dlqInsertFailures.Should().Be(1);
            capturedMessageType.Should().Be("OrderCreated");
        }
    }
}

