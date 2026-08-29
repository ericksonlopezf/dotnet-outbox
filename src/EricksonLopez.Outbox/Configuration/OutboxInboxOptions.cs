// Copyright © Erickson Lopez. MIT License.
using System;

namespace EricksonLopez.Outbox;

/// <summary>
/// Represents the configuration options for the Inbox idempotency system.
/// </summary>
public sealed class OutboxInboxOptions
{
    /// <summary>
    /// Gets or sets the duration for which processed idempotency records are retained before being purged.
    /// </summary>
    public TimeSpan RetentionPeriod { get; set; } = TimeSpan.FromDays(7);

    /// <summary>
    /// Gets or sets the time window within which duplicate messages are detected.
    /// </summary>
    public TimeSpan DuplicateDetectionWindow { get; set; } = TimeSpan.FromHours(24);

    /// <summary>
    /// Gets or sets the interval between background cleanup operations for expired idempotency records.
    /// </summary>
    public TimeSpan CleanupInterval { get; set; } = TimeSpan.FromHours(1);
}
