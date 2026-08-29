// Copyright © Erickson Lopez. MIT License.
using System;

namespace EricksonLopez.Outbox;

/// <summary>
/// Configuration options for <see cref="EricksonLopez.Outbox.Hosting.OutboxCleanupService"/>.
/// </summary>
public sealed class OutboxCleanupOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether the background cleanup service is enabled.
    /// Default is <see langword="false"/> (opt-in).
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Gets or sets the retention duration for dispatched outbox messages.
    /// Messages dispatched prior to <c>UtcNow - RetentionPeriod</c> will be purged.
    /// Default is 7 days.
    /// </summary>
    public TimeSpan RetentionPeriod { get; set; } = TimeSpan.FromDays(7);

    /// <summary>
    /// Gets or sets the interval between successive cleanup execution passes.
    /// Default is 1 hour.
    /// </summary>
    public TimeSpan CleanupInterval { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// Gets or sets the maximum number of rows deleted in a single purge batch to avoid table lock escalation.
    /// Default is 1000.
    /// </summary>
    public int BatchSize { get; set; } = 1000;
}

