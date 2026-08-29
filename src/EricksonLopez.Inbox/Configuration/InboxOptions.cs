// Copyright © Erickson Lopez. MIT License.
using System;

namespace EricksonLopez.Inbox.Configuration;

/// <summary>
/// Configuration options for the Inbox idempotency and deduplication engine.
/// </summary>
public sealed class InboxOptions
{
    /// <summary>
    /// Gets or sets the duration after which processed inbox records are considered expired.
    /// Default is 7 days.
    /// </summary>
    public TimeSpan RetentionPeriod { get; set; } = TimeSpan.FromDays(7);

    /// <summary>
    /// Gets or sets the interval between background cleanup sweeps.
    /// Default is 1 hour.
    /// </summary>
    public TimeSpan CleanupInterval { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// Gets or sets whether the background cleanup service should be automatically registered and run.
    /// Default is <see langword="true"/>.
    /// </summary>
    public bool EnableAutomaticCleanup { get; set; } = true;
}
