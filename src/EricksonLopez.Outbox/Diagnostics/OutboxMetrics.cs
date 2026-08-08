using System;
using System.Diagnostics.Metrics;
using System.Diagnostics.CodeAnalysis;

namespace EricksonLopez.Outbox.Diagnostics;

/// <summary>
/// Provides strongly typed OpenTelemetry meters and instruments for the Outbox pattern.
/// </summary>
/// <remarks>
/// <para>
/// All instruments follow OpenTelemetry semantic conventions for messaging.
/// </para>
/// <para>
/// <b>Static Meter — Design Decision</b>: <see cref="Meter"/> is a static global field following
/// the same pattern used by .NET runtime metrics (e.g., <c>System.Net.Http</c>,
/// <c>System.Runtime</c>). For a library (not an application), static meters are the
/// established approach per the OpenTelemetry .NET guidance.
/// </para>
/// <para>
/// <b>Test isolation</b>: Because the <see cref="Meter"/> is static, counter values accumulate
/// across test runs within the same process. Tests that assert on metric values should use:
/// <list type="bullet">
///   <item>
///     <c>MeterListener</c> (built-in, zero-dependency): subscribe to the meter before the
///     operation under test and assert on recorded values within the listener scope.
///   </item>
///   <item>
///     <c>OpenTelemetry.Testing</c> / <c>InMemoryExporter</c>: configure a scoped exporter
///     per-test to capture only that test's metrics.
///   </item>
/// </list>
/// </para>
/// <para>
/// <b>IMeterFactory alternative (deferred)</b>: DI-scoped meters via <c>IMeterFactory</c>
/// (.NET 8+) would provide automatic test isolation. This was evaluated but deferred because:
/// it would require passing <c>IMeterFactory</c> through <c>AdaptivePoller</c>,
/// <c>OutboxChannel</c>, and all consumers — a significant API surface change for a
/// benefit that's primarily relevant to testing. This will be revisited in v2.0.
/// </para>
/// <para>
/// <b>AUDIT-FIX P1-E — Cardinality warning for <c>message_type</c> tag:</b><br/>
/// Several histograms and counters use a <c>message_type</c> tag (the alias string from
/// <c>[OutboxMessage("your.alias")]</c>). Each unique alias creates a separate time series
/// in your metrics backend (Prometheus, OTEL Collector, etc.).
/// </para>
/// <para>
/// With <b>N distinct message types</b>, each tagged instrument creates N time series.
/// For 10 message types across 6 instruments = 60 active time series — acceptable.
/// For 100+ message types across 6 instruments = 600+ active time series — may cause
/// cardinality issues in Prometheus with default limits (default: 10,000 series).
/// </para>
/// <para>
/// If cardinality becomes a concern, consider:
/// <list type="bullet">
///   <item>Using short, stable aliases (e.g., <c>"order.created.v1"</c>) instead of FQDN-style names.</item>
///   <item>Setting <c>OutboxRuntimeOptions.IncludeMessageTypeTag = false</c> to omit the tag entirely from all metrics.</item>
///   <item>Configuring Prometheus relabeling to drop the tag at the scrape level.</item>
/// </list>
/// </para>
/// </remarks>
[ExcludeFromCodeCoverage]
public sealed class OutboxMetrics : IDisposable
{

    /// <summary>
    /// The canonical name of the Outbox meter.
    /// </summary>
    public const string MeterName = "EricksonLopez.Outbox";

    /// <summary>
    /// Gets the meter instance used to create OpenTelemetry instruments.
    /// </summary>
    public Meter Meter { get; }

    /// <summary>
    /// Gets the counter tracking the total number of messages successfully dispatched to the broker.
    /// </summary>
    public Counter<long> MessagesDispatched { get; }

    /// <summary>
    /// Gets the counter tracking the total dispatch failures (transient + fatal combined).
    /// </summary>
    public Counter<long> DispatchFailures { get; }

    /// <summary>
    /// Gets the counter tracking the total number of messages moved to the dead-letter queue after exhausting all retry attempts.
    /// </summary>
    public Counter<long> DeadLettersTotal { get; }

    /// <summary>
    /// Gets the counter tracking the total number of retry attempts performed by the dispatcher.
    /// </summary>
    public Counter<long> RetryAttemptsTotal { get; }

    /// <summary>
    /// Gets the histogram tracking the time elapsed between message creation and its dispatch attempt (queue latency).
    /// </summary>
    public Histogram<double> QueueDuration { get; }

    /// <summary>
    /// Gets the histogram tracking the time taken by the broker publisher to send a single message over the network.
    /// </summary>
    public Histogram<double> DispatchDuration { get; }

    /// <summary>
    /// Gets the histogram tracking the time taken to build and store a message into the database.
    /// </summary>
    public Histogram<double> StoreDuration { get; }

    /// <summary>
    /// Gets the counter tracking the total number of messages reclaimed from the 'InFlight' state due to dispatcher crashes or timeouts.
    /// </summary>
    public Counter<long> ReclaimedMessages { get; }

    /// <summary>
    /// Gets the histogram tracking the number of messages fetched in a single database polling batch.
    /// </summary>
    public Histogram<int> BatchSize { get; }

    /// <summary>
    /// Gets the counter tracking the total number of DLQ INSERT failures.
    /// </summary>
    /// <remarks>
    /// A non-zero value means a message was dead-lettered in the outbox table (state=4) but
    /// the corresponding record could NOT be written to the dead-letter table (e.g., the DLQ
    /// table does not exist, or a constraint failed). The dead-letter data is lost from the
    /// DLQ, but the outbox message will not be re-processed. Monitor for spikes and set alerts.
    /// </remarks>
    public Counter<long> DlqInsertFailures { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="OutboxMetrics"/> class.
    /// </summary>
    /// <param name="meterFactory">An optional meter factory to resolve the meter instance.</param>
    public OutboxMetrics(IMeterFactory? meterFactory = null)
    {
        Meter = meterFactory?.Create(new MeterOptions(MeterName) { Version = "1.0.0" }) 
                ?? new Meter(MeterName, "1.0.0");

        MessagesDispatched = Meter.CreateCounter<long>("messaging.outbox.messages.dispatched", "{message}", "Total messages successfully dispatched to the broker.");
        DispatchFailures = Meter.CreateCounter<long>("messaging.outbox.dispatch.errors", "{message}", "Total dispatch failures. Tag 'error.type' = 'transient' | 'fatal'.");
        DeadLettersTotal = Meter.CreateCounter<long>("messaging.outbox.messages.dead_lettered", "{message}", "Total messages moved to the dead-letter queue after exhausting retries.");
        RetryAttemptsTotal = Meter.CreateCounter<long>("messaging.outbox.retry.attempts", "{attempt}", "Total retry attempts performed by the dispatcher.");
        
        QueueDuration = Meter.CreateHistogram<double>("messaging.outbox.message.queue_duration", "s", "Time between message creation and dispatch attempt (queue latency). Note: Exemplars (like trace_id) are automatically attached by the OpenTelemetry .NET SDK when an Activity is present.");
        DispatchDuration = Meter.CreateHistogram<double>("messaging.outbox.publish.duration", "s", "Time taken by the broker publisher to send a single message. Note: Exemplars are automatically attached by the OpenTelemetry .NET SDK.");
        StoreDuration = Meter.CreateHistogram<double>("messaging.outbox.store.duration", "s", "Time taken to build and persist an outbox message to the database. Note: Exemplars are automatically attached by the OpenTelemetry .NET SDK.");
        ReclaimedMessages = Meter.CreateCounter<long>("messaging.outbox.messages.reclaimed", "{message}", "Total messages reclaimed from InFlight state due to dispatcher crash or timeout.");
        BatchSize = Meter.CreateHistogram<int>("messaging.outbox.poller.batch_size", "{message}", "Number of messages fetched in a single polling batch.");
        DlqInsertFailures = Meter.CreateCounter<long>("messaging.outbox.dlq.insert_failures", "{message}",
            "Total failures writing a dead-letter record to the DLQ table. " +
            "A non-zero value requires ops investigation — the message ID is in the 'outbox.dispatcher' log at level Error.");
    }

    /// <summary>
    /// Creates an observable gauge to monitor the fill ratio of the outbox dispatch channel.
    /// </summary>
    /// <param name="countProvider">A function that returns the current number of items in the channel.</param>
    /// <param name="capacity">The maximum capacity of the channel.</param>
    /// <returns>An observable gauge measuring the channel fill ratio.</returns>
    public ObservableGauge<double> CreateChannelFillGauge(Func<int> countProvider, int capacity)
    {
        return Meter.CreateObservableGauge<double>(
            "messaging.outbox.channel.fill_ratio",
            observeValue: () => capacity > 0 ? (double)countProvider() / capacity : 0.0,
            unit: "1",
            description: "Current fill ratio of the outbox dispatch channel [0.0=empty, 1.0=saturated]. " +
                         "Values approaching 1.0 indicate dispatcher backpressure is active.");
    }

    /// <summary>
    /// Disposes the underlying meter instance and releases associated resources.
    /// </summary>
    public void Dispose()
    {
        Meter.Dispose();
    }
}
