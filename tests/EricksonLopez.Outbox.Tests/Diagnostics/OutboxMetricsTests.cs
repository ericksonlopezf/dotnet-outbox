// Copyright © Erickson Lopez. MIT License.
using System;
using System.Diagnostics.Metrics;
using AwesomeAssertions;
using EricksonLopez.Outbox.Diagnostics;
using NSubstitute;
using Xunit;

namespace EricksonLopez.Outbox.Tests.Diagnostics;

public class OutboxMetricsTests
{
    [Fact]
    public void Constructor_Default_InitializesMeterAndAllInstruments()
    {
        using var metrics = new OutboxMetrics();

        metrics.Meter.Should().NotBeNull();
        metrics.Meter.Name.Should().Be(OutboxMetrics.MeterName);
        metrics.Meter.Version.Should().Be("2.0.0");

        metrics.MessagesDispatched.Should().NotBeNull();
        metrics.MessagesDispatched.Name.Should().Be("messaging.outbox.messages.dispatched");
        metrics.MessagesDispatched.Unit.Should().Be("{message}");
        metrics.MessagesDispatched.Description.Should().Be("Total messages successfully dispatched to the broker.");

        metrics.DispatchFailures.Should().NotBeNull();
        metrics.DispatchFailures.Name.Should().Be("messaging.outbox.dispatch.errors");
        metrics.DispatchFailures.Unit.Should().Be("{message}");
        metrics.DispatchFailures.Description.Should().Be("Total dispatch failures. Tag 'error.type' = 'transient' | 'fatal'.");

        metrics.DeadLettersTotal.Should().NotBeNull();
        metrics.DeadLettersTotal.Name.Should().Be("messaging.outbox.messages.dead_lettered");
        metrics.DeadLettersTotal.Unit.Should().Be("{message}");
        metrics.DeadLettersTotal.Description.Should().Be("Total messages moved to the dead-letter queue after exhausting retries.");

        metrics.RetryAttemptsTotal.Should().NotBeNull();
        metrics.RetryAttemptsTotal.Name.Should().Be("messaging.outbox.retry.attempts");
        metrics.RetryAttemptsTotal.Unit.Should().Be("{attempt}");
        metrics.RetryAttemptsTotal.Description.Should().Be("Total retry attempts performed by the dispatcher.");

        metrics.QueueDuration.Should().NotBeNull();
        metrics.QueueDuration.Name.Should().Be("messaging.outbox.message.queue_duration");
        metrics.QueueDuration.Unit.Should().Be("s");
        metrics.QueueDuration.Description.Should().Be("Time between message creation and dispatch attempt (queue latency). Note: Exemplars (like trace_id) are automatically attached by the OpenTelemetry .NET SDK when an Activity is present.");

        metrics.DispatchDuration.Should().NotBeNull();
        metrics.DispatchDuration.Name.Should().Be("messaging.outbox.publish.duration");
        metrics.DispatchDuration.Unit.Should().Be("s");
        metrics.DispatchDuration.Description.Should().Be("Time taken by the broker publisher to send a single message. Note: Exemplars are automatically attached by the OpenTelemetry .NET SDK.");

        metrics.StoreDuration.Should().NotBeNull();
        metrics.StoreDuration.Name.Should().Be("messaging.outbox.store.duration");
        metrics.StoreDuration.Unit.Should().Be("s");
        metrics.StoreDuration.Description.Should().Be("Time taken to build and persist an outbox message to the database. Note: Exemplars are automatically attached by the OpenTelemetry .NET SDK.");

        metrics.ReclaimedMessages.Should().NotBeNull();
        metrics.ReclaimedMessages.Name.Should().Be("messaging.outbox.messages.reclaimed");
        metrics.ReclaimedMessages.Unit.Should().Be("{message}");
        metrics.ReclaimedMessages.Description.Should().Be("Total messages reclaimed from InFlight state due to dispatcher crash or timeout.");

        metrics.BatchSize.Should().NotBeNull();
        metrics.BatchSize.Name.Should().Be("messaging.outbox.poller.batch_size");
        metrics.BatchSize.Unit.Should().Be("{message}");
        metrics.BatchSize.Description.Should().Be("Number of messages fetched in a single polling batch.");

        metrics.DlqInsertFailures.Should().NotBeNull();
        metrics.DlqInsertFailures.Name.Should().Be("messaging.outbox.dlq.insert_failures");
        metrics.DlqInsertFailures.Unit.Should().Be("{message}");
        metrics.DlqInsertFailures.Description.Should().Be("Total failures writing a dead-letter record to the DLQ table. A non-zero value requires ops investigation — the message ID is in the 'outbox.dispatcher' log at level Error.");
    }

    [Fact]
    public void Constructor_WithCustomMeterFactory_UsesFactoryMeter()
    {
        var factory = Substitute.For<IMeterFactory>();
        var customMeter = new Meter("Custom.Outbox", "2.0.0");
        factory.Create(Arg.Any<MeterOptions>()).Returns(customMeter);

        using var metrics = new OutboxMetrics(factory);

        metrics.Meter.Should().BeSameAs(customMeter);
        factory.Received(1).Create(Arg.Is<MeterOptions>(o => o.Name == OutboxMetrics.MeterName && o.Version == "2.0.0"));
    }

    [Fact]
    public void CreateChannelFillGauge_PositiveCapacity_ObservesAccurateRatio()
    {
        using var meter = new Meter(Guid.NewGuid().ToString(), "1.0.0");
        var factory = Substitute.For<IMeterFactory>();
        factory.Create(Arg.Any<MeterOptions>()).Returns(meter);

        using var metrics = new OutboxMetrics(factory);
        int currentCount = 50;
        int capacity = 200;

        var gauge = metrics.CreateChannelFillGauge(() => currentCount, capacity);
        gauge.Should().NotBeNull();
        gauge.Name.Should().Be("messaging.outbox.channel.fill_ratio");
        gauge.Unit.Should().Be("1");
        gauge.Description.Should().Be("Current fill ratio of the outbox dispatch channel [0.0=empty, 1.0=saturated]. Values approaching 1.0 indicate dispatcher backpressure is active.");

        double observedValue = -1.0;
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument == gauge)
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<double>((instrument, measurement, tags, state) =>
        {
            if (instrument == gauge)
            {
                observedValue = measurement;
            }
        });
        listener.Start();

        listener.RecordObservableInstruments();

        observedValue.Should().Be(0.25); // 50 / 200 = 0.25
    }

    [Fact]
    public void CreateChannelFillGauge_ZeroOrNegativeCapacity_ReturnsZero()
    {
        using var meter = new Meter(Guid.NewGuid().ToString(), "1.0.0");
        var factory = Substitute.For<IMeterFactory>();
        factory.Create(Arg.Any<MeterOptions>()).Returns(meter);

        using var metrics = new OutboxMetrics(factory);
        int currentCount = 50;

        var gaugeZero = metrics.CreateChannelFillGauge(() => currentCount, 0);
        var gaugeNegative = metrics.CreateChannelFillGauge(() => currentCount, -10);

        double observedZero = -1.0;
        double observedNegative = -1.0;

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument == gaugeZero || instrument == gaugeNegative)
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<double>((instrument, measurement, tags, state) =>
        {
            if (instrument == gaugeZero) observedZero = measurement;
            if (instrument == gaugeNegative) observedNegative = measurement;
        });
        listener.Start();

        listener.RecordObservableInstruments();

        observedZero.Should().Be(0.0);
        observedNegative.Should().Be(0.0);
    }

    private sealed class TrackingMeter : Meter
    {
        public bool IsDisposed { get; private set; }

        public TrackingMeter(string name, string version) : base(name, version)
        {
        }

        protected override void Dispose(bool disposing)
        {
            IsDisposed = true;
            base.Dispose(disposing);
        }
    }

    [Fact]
    public void Dispose_DisposesUnderlyingMeter()
    {
        var trackingMeter = new TrackingMeter(Guid.NewGuid().ToString(), "1.0.0");
        var factory = Substitute.For<IMeterFactory>();
        factory.Create(Arg.Any<MeterOptions>()).Returns(trackingMeter);

        var metrics = new OutboxMetrics(factory);
        trackingMeter.IsDisposed.Should().BeFalse();

        metrics.Dispose();

        trackingMeter.IsDisposed.Should().BeTrue();
    }

    [Fact]
    public void RecordStoreDuration_RecordsMeasurementWithMessageTypeTag()
    {
        using var metrics = new OutboxMetrics();
        double recordedDuration = 0;
        string? recordedTag = null;

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument == metrics.StoreDuration)
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<double>((instrument, measurement, tags, state) =>
        {
            if (instrument == metrics.StoreDuration)
            {
                recordedDuration = measurement;
                foreach (var tag in tags)
                {
                    if (tag.Key == "message_type")
                    {
                        recordedTag = tag.Value?.ToString();
                    }
                }
            }
        });
        listener.Start();

        metrics.RecordStoreDuration(0.42, "OrderCreatedEvent");

        recordedDuration.Should().Be(0.42);
        recordedTag.Should().Be("OrderCreatedEvent");
    }
}

