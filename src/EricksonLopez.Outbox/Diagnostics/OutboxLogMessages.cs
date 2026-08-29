// Copyright © Erickson Lopez. MIT License.
using System;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace EricksonLopez.Outbox.Diagnostics;

/// <summary>
/// Source-generated log messages (via <c>[LoggerMessage]</c> attribute) for all hot-path log calls.
///
/// <para>
/// Why: The previous implementation used direct _logger.LogXxx() calls everywhere, which allocate
/// a params object[] for each call site on every invocation, even if logging is disabled.
/// [LoggerMessage] generates static delegates at compile time that short-circuit on disabled
/// log levels with zero allocation.
/// </para>
///
/// <para>
/// All hot-path messages (dispatched, failed, dead-lettered, retried) must use this pattern.
/// </para>
///
/// <para>
/// <b>Note for contributors (G13.1):</b> Declared <c>static partial</c> because the
/// C# compiler requires <c>partial</c> for source-generated <c>[LoggerMessage]</c> methods.
/// The compiler generates a companion file (<c>OutboxLogMessages.g.cs</c>) in <c>obj/</c>.
/// There is <b>no manually-authored counterpart file</b> — this file is the single source of truth
/// for all log message definitions. Do not create another <c>partial class OutboxLogMessages</c> file.
/// </para>
/// </summary>
public static partial class OutboxLogMessages
{
    // --- Dispatcher hot-path messages ---

    /// <summary>Logs the MessageDispatched event.</summary>
    [LoggerMessage(
        EventId = 10000,
        EventName = "MessageDispatched",
        Level = LogLevel.Debug,
        Message = "Message {MessageId} ({MessageType}) dispatched in {ElapsedMs}ms.")]
    public static partial void MessageDispatched(
        this ILogger logger,
        Guid messageId,
        string messageType,
        long elapsedMs);

    /// <summary>Logs the MessageDispatchFailed event.</summary>
    [LoggerMessage(
        EventId = 10001,
        EventName = "MessageDispatchFailed",
        Level = LogLevel.Error,
        Message = "Failed to dispatch message {MessageId} ({MessageType}).")]
    public static partial void MessageDispatchFailed(
        this ILogger logger,
        Exception exception,
        Guid messageId,
        string messageType);

    /// <summary>Logs the MessageDeadLettered event.</summary>
    [LoggerMessage(
        EventId = 10002,
        EventName = "MessageDeadLettered",
        Level = LogLevel.Warning,
        Message = "Message {MessageId} ({MessageType}) dead-lettered after {RetryCount} retries.")]
    public static partial void MessageDeadLettered(
        this ILogger logger,
        Guid messageId,
        string messageType,
        int retryCount);

    /// <summary>Logs the DlqInsertFailed event.</summary>
    [LoggerMessage(
        EventId = 10003,
        EventName = "DlqInsertFailed",
        Level = LogLevel.Error,
        Message = "Failed to insert message {MessageId} ({MessageType}) into DLQ. Message will be marked as dead-lettered in the outbox (state=4) to prevent reprocessing, but the DLQ record is missing.")]
    public static partial void DlqInsertFailed(
        this ILogger logger,
        Exception exception,
        Guid messageId,
        string messageType);

    /// <summary>Logs the MessageRetried event.</summary>
    [LoggerMessage(
        EventId = 10004,
        EventName = "MessageRetried",
        Level = LogLevel.Warning,
        Message = "Message {MessageId} ({MessageType}) will be retried (attempt {Attempt} of {MaxRetries}).")]
    public static partial void MessageRetried(
        this ILogger logger,
        Guid messageId,
        string messageType,
        int attempt,
        int maxRetries);

    /// <summary>Logs the ChannelCancelled event.</summary>
    [LoggerMessage(
        EventId = 10005,
        EventName = "ChannelCancelled",
        Level = LogLevel.Information,
        Message = "OutboxChannel message processing cancelled (graceful shutdown).")]
    public static partial void ChannelCancelled(this ILogger logger);

    /// <summary>Logs the PayloadTooLarge event.</summary>
    [LoggerMessage(
        EventId = 10006,
        EventName = "PayloadTooLarge",
        Level = LogLevel.Warning,
        Message = "Payload for message {MessageId} is too large ({Length} bytes). Message will be dead-lettered.")]
    public static partial void PayloadTooLarge(
        this ILogger logger,
        Guid messageId,
        int length);

    /// <summary>Logs the HeadersTooLarge event.</summary>
    [LoggerMessage(
        EventId = 10007,
        EventName = "HeadersTooLarge",
        Level = LogLevel.Warning,
        Message = "Headers for message {MessageId} are too large ({Length} bytes). Message will be dead-lettered.")]
    public static partial void HeadersTooLarge(
        this ILogger logger,
        Guid messageId,
        int length);

    /// <summary>Logs the HeadersDeserializeFailed event.</summary>
    [LoggerMessage(
        EventId = 10008,
        EventName = "HeadersDeserializeFailed",
        Level = LogLevel.Warning,
        Message = "Failed to deserialize headers for message {MessageId}.")]
    public static partial void HeadersDeserializeFailed(
        this ILogger logger,
        Exception exception,
        Guid messageId);

    // --- Startup / configuration messages ---

    /// <summary>Logs the StartupValidationFailed event.</summary>
    [LoggerMessage(
        EventId = 10100,
        EventName = "StartupValidationFailed",
        Level = LogLevel.Critical,
        Message = "Outbox startup validation failed ({ErrorCount} error(s)): {Errors}")]
    public static partial void StartupValidationFailed(
        this ILogger logger,
        int errorCount,
        string errors);

    /// <summary>Logs the StartupValidationPassed event.</summary>
    [LoggerMessage(
        EventId = 10101,
        EventName = "StartupValidationPassed",
        Level = LogLevel.Debug,
        Message = "Outbox startup validation passed. All critical dependencies are registered.")]
    public static partial void StartupValidationPassed(this ILogger logger);

    /// <summary>Logs the ProducerOnlyMode event.</summary>
    [LoggerMessage(
        EventId = 10102,
        EventName = "ProducerOnlyMode",
        Level = LogLevel.Information,
        Message = "This application acts only as a publisher. Pending messages won't be dispatched.")]
    public static partial void ProducerOnlyMode(this ILogger logger);

    /// <summary>Logs the ThirdPartyDeadLetterRepositoryRegistered event.</summary>
    /// <remarks>
    /// AUDIT-FIX: Renumbered from 10103 to 10112 to resolve EventId collision.
    /// EventId 10103 was previously assigned to both DispatcherStarting (L389) and this method,
    /// causing log aggregators to incorrectly merge the two unrelated event streams.
    /// </remarks>
    [LoggerMessage(
        EventId = 10112,
        EventName = "ThirdPartyDeadLetterRepositoryRegistered",
        Level = LogLevel.Warning,
        Message = "A third-party IDeadLetterRepository ({RepositoryType}) was registered. Ensure that its InsertAsync method handles transaction=null gracefully. If it doesn't, dead lettering may fail silently.")]
    public static partial void ThirdPartyDeadLetterRepositoryRegistered(this ILogger logger, string repositoryType);

    /// <summary>
    /// Logs a DLQ payload fallback record when the DLQ INSERT fails and the message payload
    /// fits within the configured safe size limit.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>F-04 AUDIT FIX — DLQ INSERT failure recovery:</b><br/>
    /// When <c>IDeadLetterRepository.InsertAsync</c> throws, the dead-letter record is lost from the
    /// DLQ table. Without this log, operators would only know the message ID (from DlqInsertFailed)
    /// but would have no access to the original payload for manual recovery or replay.
    /// </para>
    /// <para>
    /// This log emits the message payload as a structured field so that log aggregators (Seq, Loki,
    /// Elasticsearch, Azure Monitor) can be queried for all <c>DlqPayloadFallback</c> events and the
    /// payloads can be replayed without requiring access to the database.
    /// </para>
    /// <para>
    /// <b>Security note:</b> The payload is written to logs as-is (truncated to <c>MaxPayloadSizeInBytes</c>
    /// to prevent log flooding). If payloads contain PII or secrets, ensure your log aggregator
    /// applies appropriate access controls and retention policies to this event stream.
    /// </para>
    /// <para>
    /// This log is suppressed when the payload exceeds <c>OutboxRuntimeOptions.MaxPayloadSizeInBytes</c>
    /// to prevent single large messages from flooding the log infrastructure.
    /// </para>
    /// </remarks>
    [LoggerMessage(
        EventId = 10012,
        EventName = "DlqPayloadFallback",
        Level = LogLevel.Error,
        Message = "DLQ INSERT FAILED — Payload fallback record for manual recovery. " +
                  "MessageId={MessageId} MessageType={MessageType} RetryCount={RetryCount} " +
                  "Reason={Reason} Payload={PayloadJson} " +
                  "ACTION REQUIRED: Replay or manually insert this record into the DLQ table.")]
    public static partial void DlqPayloadFallback(
        this ILogger logger,
        Guid messageId,
        string messageType,
        int retryCount,
        string reason,
        string payloadJson);

    // --- Poller messages ---

    /// <summary>Logs the PollerStarted event.</summary>
    [LoggerMessage(
        EventId = 10200,
        EventName = "PollerStarted",
        Level = LogLevel.Information,
        Message = "Outbox poller started. BatchSize={BatchSize}, Interval={Interval}ms, MaxDOP={MaxDop}.")]
    public static partial void PollerStarted(
        this ILogger logger,
        int batchSize,
        double interval,
        int maxDop);

    /// <summary>Logs the PollerStopped event.</summary>
    [LoggerMessage(
        EventId = 10201,
        EventName = "PollerStopped",
        Level = LogLevel.Information,
        Message = "Outbox poller stopped (graceful shutdown).")]
    public static partial void PollerStopped(this ILogger logger);

    /// <summary>Logs the PollerError event.</summary>
    [LoggerMessage(
        EventId = 10202,
        EventName = "PollerError",
        Level = LogLevel.Error,
        Message = "Unhandled error in outbox poller loop.")]
    public static partial void PollerError(this ILogger logger, Exception exception);

    /// <summary>Logs the BatchFetched event.</summary>
    [LoggerMessage(
        EventId = 10203,
        EventName = "BatchFetched",
        Level = LogLevel.Debug,
        Message = "Fetched {Count} messages from outbox in {ElapsedMs}ms.")]
    public static partial void BatchFetched(this ILogger logger, int count, long elapsedMs);

    /// <summary>Logs the ReclaimedStaleMessages event.</summary>
    [LoggerMessage(
        EventId = 10204,
        EventName = "ReclaimedStaleMessages",
        Level = LogLevel.Warning,
        Message = "Reclaimed {Count} stale InFlight messages back to Pending.")]
    public static partial void ReclaimedStaleMessages(this ILogger logger, int count);

    // --- Idempotency / inbox messages ---

    /// <summary>Logs the InboxCleanupStarted event.</summary>
    [LoggerMessage(
        EventId = 10300,
        EventName = "InboxCleanupStarted",
        Level = LogLevel.Information,
        Message = "Inbox Cleanup Service started. Retention window: {RetentionPeriod}. Cleanup interval: {CleanupInterval}.")]
    public static partial void InboxCleanupStarted(
        this ILogger logger,
        TimeSpan retentionPeriod,
        TimeSpan cleanupInterval);

    /// <summary>Logs the InboxCleanupPurged event.</summary>
    [LoggerMessage(
        EventId = 10301,
        EventName = "InboxCleanupPurged",
        Level = LogLevel.Debug,
        Message = "Purged idempotency records older than {Cutoff}.")]
    public static partial void InboxCleanupPurged(this ILogger logger, DateTimeOffset cutoff);

    /// <summary>Logs the InboxCleanupError event.</summary>
    [LoggerMessage(
        EventId = 10302,
        EventName = "InboxCleanupError",
        Level = LogLevel.Error,
        Message = "Error occurred during inbox cleanup.")]
    public static partial void InboxCleanupError(this ILogger logger, Exception exception);

    /// <summary>Logs the InboxDuplicateDetected event.</summary>
    [LoggerMessage(
        EventId = 10303,
        EventName = "InboxDuplicateDetected",
        Level = LogLevel.Debug,
        Message = "Duplicate message {MessageId} detected for consumer {ConsumerId}. Skipping.")]
    public static partial void InboxDuplicateDetected(
        this ILogger logger,
        Guid messageId,
        string consumerId);

    /// <summary>Logs the MessageDelayedNoRetry event.</summary>
    [LoggerMessage(
        EventId = 10009,
        EventName = "MessageDelayedNoRetry",
        Level = LogLevel.Warning,
        Message = "Message {MessageId} delayed without incrementing retry count (circuit breaker open or explicit signal). It will be reclaimed automatically after the stale timeout.")]
    public static partial void MessageDelayedNoRetry(
        this ILogger logger,
        Guid messageId);

    // P1-FIX: Source-generate the DB retry warning to eliminate params object[] allocation in hot path.
    /// <summary>Logs the DbRetryAttempt event.</summary>
    [LoggerMessage(
        EventId = 10010,
        EventName = "DbRetryAttempt",
        Level = LogLevel.Warning,
        Message = "Transient error updating outbox database. Retrying attempt {Attempt} of {MaxAttempts}.")]
    public static partial void DbRetryAttempt(
        this ILogger logger,
        Exception exception,
        int attempt,
        int maxAttempts);

    // P1-FIX: Source-generate dispatcher lifecycle logs to eliminate params object[] allocations.
    /// <summary>Logs the DispatcherStarting event.</summary>
    [LoggerMessage(
        EventId = 10103,
        EventName = "DispatcherStarting",
        Level = LogLevel.Information,
        Message = "Outbox Dispatcher starting. Parallelism={MaxDOP}, BatchSize={BatchSize}, Adaptive={Adaptive}.")]
    public static partial void DispatcherStarting(
        this ILogger logger,
        int maxDOP,
        int batchSize,
        bool adaptive);

    /// <summary>Logs the DispatcherStopped event.</summary>
    [LoggerMessage(
        EventId = 10104,
        EventName = "DispatcherStopped",
        Level = LogLevel.Information,
        Message = "Outbox Dispatcher stopped.")]
    public static partial void DispatcherStopped(this ILogger logger);

    /// <summary>Logs the DispatcherConsumerCrashed event.</summary>
    [LoggerMessage(
        EventId = 10105,
        EventName = "DispatcherConsumerCrashed",
        Level = LogLevel.Error,
        Message = "Outbox dispatch consumer #{ConsumerId} crashed. Restarting in 5s...")]
    public static partial void DispatcherConsumerCrashed(
        this ILogger logger,
        Exception exception,
        int consumerId);

    /// <summary>Logs the DispatcherConsumerStarted event.</summary>
    [LoggerMessage(
        EventId = 10106,
        EventName = "DispatcherConsumerStarted",
        Level = LogLevel.Debug,
        Message = "Outbox dispatch consumer #{ConsumerId} started.")]
    public static partial void DispatcherConsumerStarted(
        this ILogger logger,
        int consumerId);

    /// <summary>Logs the DispatcherConsumerStopped event.</summary>
    [LoggerMessage(
        EventId = 10107,
        EventName = "DispatcherConsumerStopped",
        Level = LogLevel.Debug,
        Message = "Outbox dispatch consumer #{ConsumerId} stopped.")]
    public static partial void DispatcherConsumerStopped(
        this ILogger logger,
        int consumerId);

    /// <summary>
    /// Logs when a broker publisher returns <c>default(DispatchResult)</c>, which is an invalid state.
    /// Source-generated to avoid params object[] allocation on every warning-level check.
    /// </summary>
    [LoggerMessage(
        EventId = 10011,
        EventName = "InvalidDispatchResultDetected",
        Level = LogLevel.Warning,
        Message = "IBrokerPublisher returned default(DispatchResult) for message {MessageId} ({MessageType}). " +
                  "This is treated as FailFatal(null). Ensure your publisher returns DispatchResult.Ok(), " +
                  "DispatchResult.FailAndRetry(ex), or DispatchResult.FailFatal(ex).")]
    public static partial void InvalidDispatchResultDetected(
        this ILogger logger,
        Guid messageId,
        string messageType);

}



