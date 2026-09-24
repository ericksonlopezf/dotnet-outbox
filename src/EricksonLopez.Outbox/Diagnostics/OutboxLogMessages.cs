// Copyright © Erickson Lopez. MIT License.
using System;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace EricksonLopez.Outbox.Diagnostics;

/// <summary>
/// Provides source-generated log messages (via <c>[LoggerMessage]</c> attribute) for all hot-path log calls.
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

    /// <summary>Logs the successful dispatch of an outbox message to the message broker.</summary>
    /// <param name="logger">The logger instance to write the event to.</param>
    /// <param name="messageId">The unique identifier of the dispatched message.</param>
    /// <param name="messageType">The message type alias or CLR type name.</param>
    /// <param name="elapsedMs">The elapsed duration of the dispatch operation in milliseconds.</param>
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

    /// <summary>Logs a failure encountered while dispatching an outbox message to the message broker.</summary>
    /// <param name="logger">The logger instance to write the event to.</param>
    /// <param name="exception">The exception that caused the dispatch failure.</param>
    /// <param name="messageId">The unique identifier of the message that failed to dispatch.</param>
    /// <param name="messageType">The message type alias or CLR type name.</param>
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

    /// <summary>Logs when an outbox message exhausts all retry attempts and is moved to the dead-letter queue.</summary>
    /// <param name="logger">The logger instance to write the event to.</param>
    /// <param name="messageId">The unique identifier of the dead-lettered message.</param>
    /// <param name="messageType">The message type alias or CLR type name.</param>
    /// <param name="retryCount">The total number of retry attempts made before dead-lettering.</param>
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

    /// <summary>Logs a failure encountered while inserting a message into the dead-letter queue repository.</summary>
    /// <param name="logger">The logger instance to write the event to.</param>
    /// <param name="exception">The exception that occurred during insertion into the dead-letter repository.</param>
    /// <param name="messageId">The unique identifier of the message.</param>
    /// <param name="messageType">The message type alias or CLR type name.</param>
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

    /// <summary>Logs when an outbox message dispatch fails transiently and is scheduled for a retry attempt.</summary>
    /// <param name="logger">The logger instance to write the event to.</param>
    /// <param name="messageId">The unique identifier of the message to retry.</param>
    /// <param name="messageType">The message type alias or CLR type name.</param>
    /// <param name="attempt">The current retry attempt number.</param>
    /// <param name="maxRetries">The maximum allowed retry attempts before dead-lettering.</param>
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

    /// <summary>Logs when message processing across the outbox channel is cancelled during graceful shutdown.</summary>
    /// <param name="logger">The logger instance to write the event to.</param>
    [LoggerMessage(
        EventId = 10005,
        EventName = "ChannelCancelled",
        Level = LogLevel.Information,
        Message = "OutboxChannel message processing cancelled (graceful shutdown).")]
    public static partial void ChannelCancelled(this ILogger logger);

    /// <summary>Logs when an outbox message payload exceeds the configured maximum allowed size.</summary>
    /// <param name="logger">The logger instance to write the event to.</param>
    /// <param name="messageId">The unique identifier of the message.</param>
    /// <param name="length">The payload size in bytes.</param>
    [LoggerMessage(
        EventId = 10006,
        EventName = "PayloadTooLarge",
        Level = LogLevel.Warning,
        Message = "Payload for message {MessageId} is too large ({Length} bytes). Message will be dead-lettered.")]
    public static partial void PayloadTooLarge(
        this ILogger logger,
        Guid messageId,
        int length);

    /// <summary>Logs when serialized outbox message headers exceed the configured maximum allowed size.</summary>
    /// <param name="logger">The logger instance to write the event to.</param>
    /// <param name="messageId">The unique identifier of the message.</param>
    /// <param name="length">The serialized headers size in bytes.</param>
    [LoggerMessage(
        EventId = 10007,
        EventName = "HeadersTooLarge",
        Level = LogLevel.Warning,
        Message = "Headers for message {MessageId} are too large ({Length} bytes). Message will be dead-lettered.")]
    public static partial void HeadersTooLarge(
        this ILogger logger,
        Guid messageId,
        int length);

    /// <summary>Logs a failure encountered when deserializing message headers from binary or JSON format.</summary>
    /// <param name="logger">The logger instance to write the event to.</param>
    /// <param name="exception">The exception that occurred during header deserialization.</param>
    /// <param name="messageId">The unique identifier of the message.</param>
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

    /// <summary>Logs when outbox startup validation encounters critical configuration or registration errors.</summary>
    /// <param name="logger">The logger instance to write the event to.</param>
    /// <param name="errorCount">The total number of validation errors detected.</param>
    /// <param name="errors">A concatenated description of all validation error messages.</param>
    [LoggerMessage(
        EventId = 10100,
        EventName = "StartupValidationFailed",
        Level = LogLevel.Critical,
        Message = "Outbox startup validation failed ({ErrorCount} error(s)): {Errors}")]
    public static partial void StartupValidationFailed(
        this ILogger logger,
        int errorCount,
        string errors);

    /// <summary>Logs when outbox startup validation completes successfully with all dependencies verified.</summary>
    /// <param name="logger">The logger instance to write the event to.</param>
    [LoggerMessage(
        EventId = 10101,
        EventName = "StartupValidationPassed",
        Level = LogLevel.Debug,
        Message = "Outbox startup validation passed. All critical dependencies are registered.")]
    public static partial void StartupValidationPassed(this ILogger logger);

    /// <summary>Logs when the application is configured in producer-only mode with background dispatching disabled.</summary>
    /// <param name="logger">The logger instance to write the event to.</param>
    [LoggerMessage(
        EventId = 10102,
        EventName = "ProducerOnlyMode",
        Level = LogLevel.Information,
        Message = "This application acts only as a publisher. Pending messages won't be dispatched.")]
    public static partial void ProducerOnlyMode(this ILogger logger);

    /// <summary>Logs a warning when a custom third-party dead-letter repository is registered in dependency injection.</summary>
    /// <param name="logger">The logger instance to write the event to.</param>
    /// <param name="repositoryType">The CLR type name of the registered dead-letter repository.</param>
    /// <remarks>
    /// AUDIT-FIX: Renumbered from 10103 to 10112 to resolve EventId collision.
    /// EventId 10103 was previously assigned to both DispatcherStarting and ThirdPartyDeadLetterRepositoryRegistered,
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
    /// <param name="logger">The logger instance to write the event to.</param>
    /// <param name="messageId">The unique identifier of the message.</param>
    /// <param name="messageType">The message type alias or CLR type name.</param>
    /// <param name="retryCount">The retry count reached before dead-lettering.</param>
    /// <param name="reason">The failure reason explaining why the message was dead-lettered.</param>
    /// <param name="payloadJson">The JSON-serialized payload preserved for operator recovery.</param>
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

    /// <summary>Logs when the outbox background poller starts polling the database for pending messages.</summary>
    /// <param name="logger">The logger instance to write the event to.</param>
    /// <param name="batchSize">The maximum number of messages retrieved per batch.</param>
    /// <param name="interval">The polling interval duration in milliseconds.</param>
    /// <param name="maxDop">The maximum degree of parallelism configured for consumer workers.</param>
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

    /// <summary>Logs when the outbox background poller stops execution during graceful shutdown.</summary>
    /// <param name="logger">The logger instance to write the event to.</param>
    [LoggerMessage(
        EventId = 10201,
        EventName = "PollerStopped",
        Level = LogLevel.Information,
        Message = "Outbox poller stopped (graceful shutdown).")]
    public static partial void PollerStopped(this ILogger logger);

    /// <summary>Logs an unhandled error encountered in the outbox poller polling loop.</summary>
    /// <param name="logger">The logger instance to write the event to.</param>
    /// <param name="exception">The unhandled exception thrown in the poller loop.</param>
    [LoggerMessage(
        EventId = 10202,
        EventName = "PollerError",
        Level = LogLevel.Error,
        Message = "Unhandled error in outbox poller loop.")]
    public static partial void PollerError(this ILogger logger, Exception exception);

    /// <summary>Logs the retrieval of a batch of pending messages from the outbox database table.</summary>
    /// <param name="logger">The logger instance to write the event to.</param>
    /// <param name="count">The number of messages fetched in the batch.</param>
    /// <param name="elapsedMs">The elapsed duration of the database fetch in milliseconds.</param>
    [LoggerMessage(
        EventId = 10203,
        EventName = "BatchFetched",
        Level = LogLevel.Debug,
        Message = "Fetched {Count} messages from outbox in {ElapsedMs}ms.")]
    public static partial void BatchFetched(this ILogger logger, int count, long elapsedMs);

    /// <summary>Logs when stale in-flight messages are reclaimed back to the pending state for redelivery.</summary>
    /// <param name="logger">The logger instance to write the event to.</param>
    /// <param name="count">The number of stale messages reset to pending status.</param>
    [LoggerMessage(
        EventId = 10204,
        EventName = "ReclaimedStaleMessages",
        Level = LogLevel.Warning,
        Message = "Reclaimed {Count} stale InFlight messages back to Pending.")]
    public static partial void ReclaimedStaleMessages(this ILogger logger, int count);

    // --- Idempotency / inbox messages ---

    /// <summary>Logs the startup of the inbox cleanup background service.</summary>
    /// <param name="logger">The logger instance to write the event to.</param>
    /// <param name="retentionPeriod">The retention time span after which processed idempotency records are purged.</param>
    /// <param name="cleanupInterval">The interval frequency at which cleanup purge executions run.</param>
    [LoggerMessage(
        EventId = 10300,
        EventName = "InboxCleanupStarted",
        Level = LogLevel.Information,
        Message = "Inbox Cleanup Service started. Retention window: {RetentionPeriod}. Cleanup interval: {CleanupInterval}.")]
    public static partial void InboxCleanupStarted(
        this ILogger logger,
        TimeSpan retentionPeriod,
        TimeSpan cleanupInterval);

    /// <summary>Logs the successful purge of expired idempotency records from the inbox store.</summary>
    /// <param name="logger">The logger instance to write the event to.</param>
    /// <param name="cutoff">The threshold timestamp before which all records were purged.</param>
    [LoggerMessage(
        EventId = 10301,
        EventName = "InboxCleanupPurged",
        Level = LogLevel.Debug,
        Message = "Purged idempotency records older than {Cutoff}.")]
    public static partial void InboxCleanupPurged(this ILogger logger, DateTimeOffset cutoff);

    /// <summary>Logs an error encountered during the inbox cleanup execution cycle.</summary>
    /// <param name="logger">The logger instance to write the event to.</param>
    /// <param name="exception">The exception that occurred during inbox cleanup.</param>
    [LoggerMessage(
        EventId = 10302,
        EventName = "InboxCleanupError",
        Level = LogLevel.Error,
        Message = "Error occurred during inbox cleanup.")]
    public static partial void InboxCleanupError(this ILogger logger, Exception exception);

    /// <summary>Logs when a duplicate incoming message is detected and skipped by the inbox idempotency checker.</summary>
    /// <param name="logger">The logger instance to write the event to.</param>
    /// <param name="messageId">The unique identifier of the duplicate message.</param>
    /// <param name="consumerId">The identifier of the consumer processing the message.</param>
    [LoggerMessage(
        EventId = 10303,
        EventName = "InboxDuplicateDetected",
        Level = LogLevel.Debug,
        Message = "Duplicate message {MessageId} detected for consumer {ConsumerId}. Skipping.")]
    public static partial void InboxDuplicateDetected(
        this ILogger logger,
        Guid messageId,
        string consumerId);

    /// <summary>Logs when a message is delayed without incrementing its retry count due to an open circuit breaker.</summary>
    /// <param name="logger">The logger instance to write the event to.</param>
    /// <param name="messageId">The unique identifier of the delayed message.</param>
    [LoggerMessage(
        EventId = 10009,
        EventName = "MessageDelayedNoRetry",
        Level = LogLevel.Warning,
        Message = "Message {MessageId} delayed without incrementing retry count (circuit breaker open or explicit signal). It will be reclaimed automatically after the stale timeout.")]
    public static partial void MessageDelayedNoRetry(
        this ILogger logger,
        Guid messageId);

    // P1-FIX: Source-generate the DB retry warning to eliminate params object[] allocation in hot path.
    /// <summary>Logs a transient database error retry attempt during an outbox persistence operation.</summary>
    /// <param name="logger">The logger instance to write the event to.</param>
    /// <param name="exception">The transient database exception that triggered the retry.</param>
    /// <param name="attempt">The current retry attempt number.</param>
    /// <param name="maxAttempts">The maximum retry attempts configured for database operations.</param>
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
    /// <summary>Logs the startup of the outbox dispatcher background service with its operational parameters.</summary>
    /// <param name="logger">The logger instance to write the event to.</param>
    /// <param name="maxDOP">The maximum degree of parallelism for concurrent dispatching.</param>
    /// <param name="batchSize">The configured batch size for message retrieval.</param>
    /// <param name="adaptive">A value indicating whether adaptive polling is enabled.</param>
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

    /// <summary>Logs when the outbox dispatcher stops processing during application shutdown.</summary>
    /// <param name="logger">The logger instance to write the event to.</param>
    [LoggerMessage(
        EventId = 10104,
        EventName = "DispatcherStopped",
        Level = LogLevel.Information,
        Message = "Outbox Dispatcher stopped.")]
    public static partial void DispatcherStopped(this ILogger logger);

    /// <summary>Logs when an outbox dispatch consumer worker crashes due to an unhandled exception.</summary>
    /// <param name="logger">The logger instance to write the event to.</param>
    /// <param name="exception">The unhandled exception that caused the consumer worker crash.</param>
    /// <param name="consumerId">The zero-based index or identifier of the crashed consumer worker.</param>
    [LoggerMessage(
        EventId = 10105,
        EventName = "DispatcherConsumerCrashed",
        Level = LogLevel.Error,
        Message = "Outbox dispatch consumer #{ConsumerId} crashed. Restarting in 5s...")]
    public static partial void DispatcherConsumerCrashed(
        this ILogger logger,
        Exception exception,
        int consumerId);

    /// <summary>Logs when an outbox dispatch consumer worker starts its processing loop.</summary>
    /// <param name="logger">The logger instance to write the event to.</param>
    /// <param name="consumerId">The zero-based index or identifier of the started consumer worker.</param>
    [LoggerMessage(
        EventId = 10106,
        EventName = "DispatcherConsumerStarted",
        Level = LogLevel.Debug,
        Message = "Outbox dispatch consumer #{ConsumerId} started.")]
    public static partial void DispatcherConsumerStarted(
        this ILogger logger,
        int consumerId);

    /// <summary>Logs when an outbox dispatch consumer worker terminates its processing loop.</summary>
    /// <param name="logger">The logger instance to write the event to.</param>
    /// <param name="consumerId">The zero-based index or identifier of the stopped consumer worker.</param>
    [LoggerMessage(
        EventId = 10107,
        EventName = "DispatcherConsumerStopped",
        Level = LogLevel.Debug,
        Message = "Outbox dispatch consumer #{ConsumerId} stopped.")]
    public static partial void DispatcherConsumerStopped(
        this ILogger logger,
        int consumerId);

    /// <summary>Logs a warning when a broker publisher returns an uninitialized default dispatch result.</summary>
    /// <param name="logger">The logger instance to write the event to.</param>
    /// <param name="messageId">The unique identifier of the message.</param>
    /// <param name="messageType">The message type alias or CLR type name.</param>
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
