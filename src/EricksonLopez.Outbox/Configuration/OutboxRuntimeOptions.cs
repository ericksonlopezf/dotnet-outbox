// Copyright © Erickson Lopez. MIT License.
using System;

namespace EricksonLopez.Outbox;

/// <summary>
/// Represents the runtime configuration options governing message processing in the outbox.
/// </summary>
public sealed class OutboxRuntimeOptions
{
    /// <summary>
    /// Gets the unique identifier for this specific instance of the outbox runtime.
    /// </summary>
    public string InstanceId { get; internal set; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// Gets or sets the database schema name where the outbox tables reside.
    /// </summary>
    public string SchemaName { get; set; } = "outbox";

    /// <summary>
    /// Gets or sets the base name of the outbox messages table.
    /// </summary>
    public string TableName { get; set; } = "messages";

    /// <summary>
    /// Gets or sets the maximum allowed size, in bytes, for an individual message payload.
    /// </summary>
    public int MaxPayloadSizeInBytes { get; set; } = 1024 * 1024;

    /// <summary>
    /// Gets or sets the maximum allowed size, in bytes, for the serialized metadata headers of a message.
    /// </summary>
    public int MaxHeaderSizeInBytes { get; set; } = 64 * 1024;

    /// <summary>
    /// Gets or sets a value indicating whether to throw an exception if an unregistered message type is encountered.
    /// </summary>
    public bool ThrowOnUnregisteredType { get; set; } = true;

    /// <summary>
    /// Gets or sets the maximum age of a message before it is eligible for cleanup or archival.
    /// </summary>
    public TimeSpan MaxMessageAge { get; set; } = TimeSpan.FromDays(30);

    /// <summary>
    /// Gets or sets the maximum exponential backoff delay, in seconds, for failed messages awaiting retry.
    /// </summary>
    public int MaxBackoffSeconds { get; set; } = 3600;

    /// <summary>
    /// Gets or sets the threshold (number of estimated rows) above which exact COUNT(*) is bypassed in favor of catalog estimates.
    /// </summary>
    public int LargeTableThreshold { get; set; } = 50_000;

    /// <summary>
    /// Gets or sets a value indicating whether dispatched messages are physically deleted from the outbox table.
    /// </summary>
    public bool DeleteOnDispatch { get; set; } = true;

    /// <summary>
    /// Gets or sets the maximum number of messages that can be stored per second via <c>IOutbox.StoreAsync</c>.
    /// A value of <c>0</c> means no limit (unbounded).
    /// </summary>
    public int MaxStoreRatePerSecond { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of stale messages (state=1 InFlight) reclaimed per reclaim cycle.
    /// </summary>
    public int ReclaimBatchLimit { get; set; } = 1000;

    /// <summary>
    /// Gets or sets a value indicating whether to include the <c>messaging.message.type</c> tag
    /// on OpenTelemetry metrics instruments (counters, histograms).
    /// </summary>
    public bool IncludeMessageTypeTag { get; set; } = true;
}
