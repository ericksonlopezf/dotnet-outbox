// Copyright © Erickson Lopez. MIT License.

namespace EricksonLopez.Outbox.Retry;

/// <summary>
/// Represents the three states of a circuit breaker.
/// </summary>
public enum CircuitState
{
    /// <summary>Normal operation. All publish calls pass through.</summary>
    Closed,
    /// <summary>Too many failures. Publish calls are rejected immediately without hitting the broker.</summary>
    Open,
    /// <summary>A probe period has elapsed. The next single call is allowed through to test broker recovery.</summary>
    HalfOpen
}
