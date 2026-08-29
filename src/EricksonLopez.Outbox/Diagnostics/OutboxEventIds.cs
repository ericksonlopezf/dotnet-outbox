// Copyright © Erickson Lopez. MIT License.
using Microsoft.Extensions.Logging;

namespace EricksonLopez.Outbox.Diagnostics;

/// <summary>
/// Centralized event IDs for all EricksonLopez.Outbox log messages.
///
/// <para>
/// Range 10000-10099: Dispatcher / channel events<br/>
/// Range 10100-10199: Startup / configuration events<br/>
/// Range 10200-10299: Poller events<br/>
/// Range 10300-10399: Idempotency / inbox events
/// </para>
/// </summary>
public static class OutboxEventIds
{
    // Dispatcher events (10000-10099)
    /// <summary>Event ID for MessageDispatched.</summary>
    public static readonly EventId MessageDispatched = new(10000, "MessageDispatched");
    /// <summary>Event ID for MessageDispatchFailed.</summary>
    public static readonly EventId MessageDispatchFailed = new(10001, "MessageDispatchFailed");
    /// <summary>Event ID for MessageDeadLettered.</summary>
    public static readonly EventId MessageDeadLettered = new(10002, "MessageDeadLettered");
    /// <summary>Event ID for DlqInsertFailed.</summary>
    public static readonly EventId DlqInsertFailed = new(10003, "DlqInsertFailed");
    /// <summary>Event ID for MessageRetried.</summary>
    public static readonly EventId MessageRetried = new(10004, "MessageRetried");
    /// <summary>Event ID for ChannelCancelled.</summary>
    public static readonly EventId ChannelCancelled = new(10005, "ChannelCancelled");
    /// <summary>Event ID for PayloadTooLarge.</summary>
    public static readonly EventId PayloadTooLarge = new(10006, "PayloadTooLarge");
    /// <summary>Event ID for HeadersTooLarge.</summary>
    public static readonly EventId HeadersTooLarge = new(10007, "HeadersTooLarge");
    /// <summary>Event ID for HeadersDeserializeFailed.</summary>
    public static readonly EventId HeadersDeserializeFailed = new(10008, "HeadersDeserializeFailed");
    /// <summary>Event ID for MessageDelayedNoRetry.</summary>
    public static readonly EventId MessageDelayedNoRetry = new(10009, "MessageDelayedNoRetry");
    /// <summary>Event ID for DbRetryAttempt.</summary>
    public static readonly EventId DbRetryAttempt = new(10010, "DbRetryAttempt");
    /// <summary>Event ID for InvalidDispatchResultDetected.</summary>
    public static readonly EventId InvalidDispatchResultDetected = new(10011, "InvalidDispatchResultDetected");
    /// <summary>
    /// Event ID for DlqPayloadFallback.
    /// Emitted when a DLQ INSERT fails and the message payload is written to the structured log
    /// as a fallback recovery record. Allows log aggregators (Seq, Loki, etc.) to act as a DLQ fallback.
    /// </summary>
    public static readonly EventId DlqPayloadFallback = new(10012, "DlqPayloadFallback");

    // Startup / configuration events (10100-10199)
    /// <summary>Event ID for StartupValidationFailed.</summary>
    public static readonly EventId StartupValidationFailed = new(10100, "StartupValidationFailed");
    /// <summary>Event ID for StartupValidationPassed.</summary>
    public static readonly EventId StartupValidationPassed = new(10101, "StartupValidationPassed");
    /// <summary>Event ID for ProducerOnlyMode.</summary>
    public static readonly EventId ProducerOnlyMode = new(10102, "ProducerOnlyMode");
    /// <summary>Event ID for DispatcherStarting.</summary>
    public static readonly EventId DispatcherStarting = new(10103, "DispatcherStarting");
    /// <summary>Event ID for DispatcherStopped.</summary>
    public static readonly EventId DispatcherStopped = new(10104, "DispatcherStopped");
    /// <summary>Event ID for DispatcherConsumerCrashed.</summary>
    public static readonly EventId DispatcherConsumerCrashed = new(10105, "DispatcherConsumerCrashed");
    /// <summary>Event ID for DispatcherConsumerStarted.</summary>
    public static readonly EventId DispatcherConsumerStarted = new(10106, "DispatcherConsumerStarted");
    /// <summary>Event ID for CircuitBreakerTripped.</summary>
    public static readonly EventId CircuitBreakerTripped = new(10107, "CircuitBreakerTripped");
    /// <summary>Event ID for CircuitBreakerReset.</summary>
    public static readonly EventId CircuitBreakerReset = new(10108, "CircuitBreakerReset");
    /// <summary>Event ID for CircuitBreakerHalfOpen.</summary>
    public static readonly EventId CircuitBreakerHalfOpen = new(10109, "CircuitBreakerHalfOpen");

    // Poller events (10200-10299)
    /// <summary>Event ID for PollerStarted.</summary>
    public static readonly EventId PollerStarted = new(10200, "PollerStarted");
    /// <summary>Event ID for PollerStopped.</summary>
    public static readonly EventId PollerStopped = new(10201, "PollerStopped");
    /// <summary>Event ID for PollerError.</summary>
    public static readonly EventId PollerError = new(10202, "PollerError");
    /// <summary>Event ID for BatchFetched.</summary>
    public static readonly EventId BatchFetched = new(10203, "BatchFetched");

    // Idempotency / inbox events (10300-10399)
    /// <summary>Event ID for InboxCleanupStarted.</summary>
    public static readonly EventId InboxCleanupStarted = new(10300, "InboxCleanupStarted");
    /// <summary>Event ID for InboxCleanupPurged.</summary>
    public static readonly EventId InboxCleanupPurged = new(10301, "InboxCleanupPurged");
    /// <summary>Event ID for InboxCleanupError.</summary>
    public static readonly EventId InboxCleanupError = new(10302, "InboxCleanupError");
    /// <summary>Event ID for InboxDuplicateDetected.</summary>
    public static readonly EventId InboxDuplicateDetected = new(10303, "InboxDuplicateDetected");
}
