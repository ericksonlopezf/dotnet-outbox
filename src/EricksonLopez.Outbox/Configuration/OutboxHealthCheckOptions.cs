// Copyright © Erickson Lopez. MIT License.

namespace EricksonLopez.Outbox;

/// <summary>
/// Represents the configuration options for the Outbox Health Check service.
/// </summary>
public sealed class OutboxHealthCheckOptions
{
    /// <summary>
    /// Gets or sets the threshold of pending messages above which the health check reports a degraded status.
    /// </summary>
    public int WarningThreshold { get; set; } = 1_000;
}
