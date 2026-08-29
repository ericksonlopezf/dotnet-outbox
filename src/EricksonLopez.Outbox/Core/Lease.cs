// Copyright © Erickson Lopez. MIT License.
using System;

namespace EricksonLopez.Outbox;

/// <summary>
/// Represents a time-bound, exclusive lease on a specific resource.
/// </summary>
/// <param name="ResourceId">The unique identifier of the leased resource.</param>
/// <param name="OwnerId">The unique identifier of the process or instance that currently holds the lease.</param>
/// <param name="ExpiresAt">The absolute UTC timestamp when the lease expires and the resource becomes available again.</param>
public readonly record struct Lease(
    string ResourceId,
    string OwnerId,
    DateTimeOffset ExpiresAt)
{
    /// <summary>
    /// Determines whether the lease has expired relative to the specified timestamp.
    /// </summary>
    /// <param name="now">The current UTC timestamp to evaluate against the lease expiration.</param>
    /// <returns><see langword="true"/> if the lease is expired; otherwise, <see langword="false"/>.</returns>
    public bool IsExpired(DateTimeOffset now) => now >= ExpiresAt;
}

