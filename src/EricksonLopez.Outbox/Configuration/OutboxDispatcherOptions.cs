// Copyright © Erickson Lopez. MIT License.
using System;

namespace EricksonLopez.Outbox;

/// <summary>
/// Represents the configuration options for the Outbox Dispatcher background service.
/// </summary>
public sealed class OutboxDispatcherOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether all registered pipeline middlewares are registered as singletons.
    /// When <see langword="true"/>, the dispatcher caches the built middleware pipeline to avoid per-batch allocations.
    /// Defaults to <see langword="false"/>.
    /// </summary>
    public bool HasOnlySingletonMiddlewares { get; set; }

    /// <summary>
    /// Gets or sets the fixed time interval to wait between polling cycles when the outbox is empty.
    /// </summary>
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Gets or sets a value indicating whether the dispatcher should dynamically adjust the polling interval based on load.
    /// </summary>
    public bool UseAdaptivePolling { get; set; } = true;

    /// <summary>
    /// Gets or sets the maximum number of messages retrieved from the database per polling cycle.
    /// </summary>
    public int BatchSize { get; set; } = 100;

    /// <summary>
    /// Gets or sets the maximum number of concurrent consumer tasks draining the dispatcher channel.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Configures the number of parallel consumer worker tasks spawned by the dispatcher.
    /// </para>
    /// <para>
    /// Default: <c>min(ProcessorCount, 8)</c>.
    /// </para>
    /// </remarks>
    public int MaxDegreeOfParallelism { get; set; } = ComputeDefaultMaxDegreeOfParallelism(Environment.ProcessorCount);

    internal static int ComputeDefaultMaxDegreeOfParallelism(int processorCount) => Math.Min(processorCount, 8);

    /// <summary>
    /// Gets or sets the maximum number of batches to process per second when adaptive polling is draining a large backlog.
    /// A value of 0 means no limit (unbounded).
    /// </summary>
    public int MaxBatchesPerSecond { get; set; }

    /// <summary>
    /// Gets or sets the maximum capacity of the in-memory channel connecting the poller to consumers.
    /// </summary>
    public int ChannelCapacity { get; set; } = 1000;

    /// <summary>
    /// Gets or sets the maximum number of retry attempts for a failed message dispatch before marking it as permanently failed.
    /// </summary>
    public int MaxRetryCount { get; set; } = 10;

    /// <summary>
    /// Gets or sets the timeout duration after which a stuck message is considered stale and available for reclamation.
    /// </summary>
    public TimeSpan ReclaimTimeout { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Gets or sets the interval between background checks for stale messages to reclaim.
    /// </summary>
    public TimeSpan ReclaimInterval { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Gets or sets the maximum number of retry attempts for a transient DB operation failure before the exception propagates.
    /// </summary>
    public int DbRetryMaxAttempts { get; set; } = 3;

    /// <summary>
    /// Gets or sets the base delay in milliseconds between DB operation retry attempts.
    /// </summary>
    public int DbRetryBaseDelayMs { get; set; } = 50;

    /// <summary>
    /// Gets or sets the interval at which the approximate pending message count is refreshed and emitted as the <c>messaging.outbox.pending.messages</c> OpenTelemetry gauge.
    /// </summary>
    public TimeSpan PendingCountRefreshInterval { get; set; } = TimeSpan.FromSeconds(30);
}
