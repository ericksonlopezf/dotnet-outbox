// Copyright © Erickson Lopez. MIT License.

namespace EricksonLopez.Outbox.Retry;

/// <summary>
/// Represents the three states of a circuit breaker.
/// </summary>
public enum CircuitState
{
    /// <summary>Indicates normal operation where all publish calls pass through.</summary>
    Closed,
    /// <summary>Indicates that failure thresholds were exceeded and publish calls are rejected immediately.</summary>
    Open,
    /// <summary>Indicates that a probe period has elapsed and the next trial call is allowed through to test broker recovery.</summary>
    HalfOpen
}
