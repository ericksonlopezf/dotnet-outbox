// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using EricksonLopez.Outbox;
using EricksonLopez.Outbox.Diagnostics;
using EricksonLopez.Outbox.Persistence;
using EricksonLopez.Outbox.Pipeline;
using EricksonLopez.Outbox.Serialization;
using EricksonLopez.Result;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EricksonLopez.Outbox.Dispatcher;

/// <summary>
/// A bounded, back-pressure-aware channel that buffers <see cref="OutboxMessage"/> items
/// fetched from the database and delivers them to the broker publisher.
///
/// SingleWriter=true: Only the AdaptivePoller writes.
/// SingleReader=true when MaxDegreeOfParallelism=1 (default): enables lock-free single-reader
///   optimization in Channels. Set to false when parallelism > 1.
/// FullMode=Wait: Ensures backpressure — the poller pauses when the channel is saturated
///   instead of dropping messages or causing OOM.
/// </summary>
/// <remarks>
/// <para><b>Access modifier: internal</b>. OutboxChannel is an implementation detail of the
/// dispatcher infrastructure. It is not part of the public API surface and should not be
/// used directly by application code or library consumers.</para>
/// </remarks>
internal sealed class OutboxChannel
{
    private readonly Channel<OutboxMessage> _channel;
    private readonly ILogger<OutboxChannel> _logger;
    private readonly IBrokerPublisher _publisher;
    private readonly OutboxDispatcherOptions _options;
    private readonly OutboxRuntimeOptions _baseOptions;
    private readonly OutboxMetrics _metrics;
    // FIX-15: ObservableGauge for channel saturation monitoring. Field kept to prevent GC collection.
    // ObservableGauge holds a weak reference to its callback; if the gauge object is GC'd, the metric
    // stops emitting. Keeping a strong reference ensures the gauge persists for the lifetime of the channel.
    // ReSharper disable once NotAccessedField.Local
    private readonly System.Diagnostics.Metrics.ObservableGauge<double> _channelFillGauge;
    private readonly OutboxPipelineDelegate _terminalDelegate;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IErrorSanitizer _errorSanitizer;
    private readonly IOutboxMiddleware[]? _cachedMiddlewares;
    // AUDIT-FIX P1-C: Pre-built pipeline instance for the singleton-middlewares fast path.
    // When HasOnlySingletonMiddlewares=true, all middlewares are singleton services that never
    // change after DI startup. The OutboxPipeline is a stateless delegate chain — it holds
    // no mutable state and is safe to share across concurrent consumers (thread-safe).
    //
    // Without this cache, ProcessMessagesAsync constructs a new OutboxPipeline per batch,
    // which creates N closures per second (one per middleware per batch). In production at
    // 1000 batches/sec with 3 middlewares = 3000 closure objects/sec going to Gen0.
    //
    // With this cache: zero allocations in the hot path for the common singleton-only case.
    private OutboxPipeline? _cachedPipeline;
    // AUDIT-FIX G2: Caches the IncludeMessageTypeTag option to avoid accessing
    // the options object on every metric emission. Evaluated once at construction.
    private readonly bool _includeMessageTypeTag;
    private readonly TimeProvider _timeProvider;

    // Headers deserialization cache is intentionally NOT a field.
    // Each concurrent consumer (ProcessMessagesAsync call) maintains its own local cache
    // as method-local variables to guarantee thread isolation.
    // See: ProcessMessagesAsync — lastHeadersMemory / lastHeadersDict locals.

    public OutboxChannel(
        ILogger<OutboxChannel> logger,
        IBrokerPublisher publisher,
        IOptions<OutboxDispatcherOptions> options,
        IOptions<OutboxRuntimeOptions> baseOptions,
        OutboxMetrics metrics,
        IServiceScopeFactory scopeFactory,
        IErrorSanitizer errorSanitizer,
        TimeProvider timeProvider)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
        _options = options?.Value ?? new OutboxDispatcherOptions();
        _baseOptions = baseOptions?.Value ?? new OutboxRuntimeOptions();
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _errorSanitizer = errorSanitizer ?? throw new ArgumentNullException(nameof(errorSanitizer));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

        _cachedMiddlewares = null;
        if (_options.HasOnlySingletonMiddlewares)
        {
            using var scope = _scopeFactory.CreateScope();
            _cachedMiddlewares = scope.ServiceProvider.GetServices<IOutboxMiddleware>().ToArray();
        }

        _includeMessageTypeTag = _baseOptions.IncludeMessageTypeTag;


        var channelOptions = CreateChannelOptions(_options);
        _channel = Channel.CreateBounded<OutboxMessage>(channelOptions);

        // FIX-15: Register channel fill ratio gauge immediately after channel creation.
        // Lambda captures _channel by ref (closure) — always reads the live Count.
        _channelFillGauge = _metrics.CreateChannelFillGauge(
            countProvider: () => _channel.Reader.Count,
            capacity: _options.ChannelCapacity);

        _terminalDelegate = async (msg, meta, ct) =>
        {
            var context = new DispatchContext(ct, attempt: msg.RetryCount + 1);
            return await _publisher.PublishRawAsync(msg, meta, context).ConfigureAwait(false);
        };


        BuildCachedPipeline();
    }

    private void BuildCachedPipeline()
    {
        if (_cachedMiddlewares is not null)
        {
            _cachedPipeline = new OutboxPipeline(_cachedMiddlewares, _terminalDelegate);
        }
    }

    public async ValueTask WriteAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        await _channel.Writer.WriteAsync(message, cancellationToken).ConfigureAwait(false);
    }

    internal void Complete()
    {
        _channel.Writer.TryComplete();
    }

    private static string GetDlqReason(DispatchResult result) => !result.ShouldRetry ? "Fatal failure" : "Max retries reached";

    public async Task ProcessMessagesAsync(
        CancellationToken cancellationToken)
    {

        try
        {
            var batch = new List<OutboxMessage>(100);
            var dispatchedIds = new HashSet<Guid>(100);

            // P0-FIX: Single-entry headers deserialization cache as METHOD-LOCAL state.
            // These MUST NOT be instance fields — with SingleReader=false, N concurrent consumers
            // call this method on the same OutboxChannel instance. Encapsulating in a per-call
            // object gives each consumer its own independent cache without synchronization overhead.
            var headersCache = new HeadersDeserializationCache();

            while (await _channel.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
            {
                batch.Clear();
                dispatchedIds.Clear();

                // Stryker disable once all 
                headersCache.Reset();

                // FIX: Micro-batch flushing
                long startTicks = Environment.TickCount64;
                FillBatchFast(batch, startTicks);


                if (batch.Count == 0) continue;


                // Metric BatchSize is recorded in AdaptivePoller to avoid double counting micro-batches

                await using var scope = _scopeFactory.CreateAsyncScope();
                // Justification for Service Locator (OUTBOX-CC2):
                // OutboxChannel runs as a singleton background consumer. To interact with the database 
                // and resolve scoped middlewares per-message/per-batch, we must resolve them from a scope.
                var repository = scope.ServiceProvider.GetRequiredService<IOutboxRepository>();
                var dlqRepository = scope.ServiceProvider.GetService<IDeadLetterRepository>();

                // FIX-10 (improved by AUDIT-FIX P1-C): Build the pipeline ONCE per batch when
                // middlewares are scoped/transient; use the pre-built singleton pipeline when possible.
                //
                // Fast path (HasOnlySingletonMiddlewares=true): _cachedPipeline is built once
                // at construction — zero allocations per batch in the hot path.
                //
                // Normal path (scoped/transient middlewares): build the pipeline once per batch
                // (not once per message as before), creating N closures + N OutboxPipeline objects
                // per batch iteration where N = number of middlewares.
                //
                // The pipeline is safe to reuse across messages in the same batch because:
                //   1. IBrokerPublisher (_publisher) is a singleton.
                //   2. Each message invocation passes its own (msg, meta, ct) — no shared mutable state.
                var pipeline = _cachedPipeline ?? new OutboxPipeline(scope.ServiceProvider.GetServices<IOutboxMiddleware>(), _terminalDelegate);

                foreach (var message in batch)
                {
                    if (cancellationToken.IsCancellationRequested) break;

                    var dispatched = await ProcessSingleMessageAsync(
                        message, pipeline, repository, dlqRepository,
                        headersCache, cancellationToken).ConfigureAwait(false);

                    if (dispatched)
                        dispatchedIds.Add(message.Id);
                }

                await FlushDispatchedAsync(dispatchedIds, batch, repository, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.ChannelCancelled();
        }
    }

    /// <summary>
    /// Dispatches a single outbox message through the pipeline and handles the result.
    /// Returns <c>true</c> if the message was successfully dispatched (should be marked dispatched);
    /// <c>false</c> if the message failed and must not be marked dispatched.
    /// </summary>
    private async ValueTask<bool> ProcessSingleMessageAsync(
        OutboxMessage message,
        OutboxPipeline pipeline,
        IOutboxRepository repository,
        IDeadLetterRepository? dlqRepository,
        HeadersDeserializationCache headersCache,
        CancellationToken cancellationToken)
    {

        DispatchResult result = default;
        bool skipExecution = false;
        string? parentTraceId = null;
        string? parentTraceState = null;

        if (!TryDeserializeHeaders(message, headersCache,
                out var headers, out parentTraceId, out parentTraceState, out result))
        {
            skipExecution = true;
        }

        using var activity = skipExecution ? null : OutboxActivitySource.StartDispatchActivity(
            message.MessageType,
            message.CorrelationId,
            parentTraceId,
            parentTraceState,
            messageId: message.Id.ToString(),
            brokerSystemName: _publisher.BrokerSystemName);

        var swTicks = System.Diagnostics.Stopwatch.GetTimestamp();

        if (!skipExecution)
        {
            var metadata = BuildMetadata(message, headers);
            result = await pipeline.ExecuteAsync(message, metadata, cancellationToken).ConfigureAwait(false);

            // P1-FIX: Guard against default(DispatchResult) — if an IBrokerPublisher
            // implementor returns default (Success=false, ShouldRetry=false, Error=null),
            // this is indistinguishable from FailFatal(null). Log a clear warning so
            // implementors can detect the bug instead of silently dead-lettering messages.
            if (result.Success && result.ShouldRetry)
            {
                result = DispatchResult.FailFatal(new InvalidOperationException($"Publisher returned Success=true AND ShouldRetry=true for message {message.Id}."));
            }
            else if (!result.Success && result.Error is null)
            {
                // P1-FIX: Use source-generated log instead of inline LogWarning to eliminate
                // params object[] allocation on every call (even when log level is disabled).
                _logger.InvalidDispatchResultDetected(message.Id, message.MessageType);
                result = DispatchResult.FailFatal(new InvalidOperationException(
                    $"IBrokerPublisher returned default(DispatchResult) for {message.MessageType}."));
            }
        }

        RecordProcessMetrics(message, skipExecution, swTicks);

        if (result.Success)
        {
            _metrics.MessagesDispatched.Add(1, MessageTypeTag(message.MessageType));
            LogMessageDispatched(message, swTicks);
            return true;
        }

        RecordDispatchFailureMetrics(result, message);
        LogMessageDispatchFailed(message, result);
        await HandleFailureAsync(message, result, repository, dlqRepository, cancellationToken).ConfigureAwait(false);
        return false;
    }

    /// <summary>
    /// Marks the dispatched messages as dispatched in the outbox repository.
    /// Uses the full-batch fast path when all messages were dispatched;
    /// otherwise builds a filtered list without LINQ allocations.
    /// </summary>
    private async ValueTask FlushDispatchedAsync(
        HashSet<Guid> dispatchedIds,
        List<OutboxMessage> batch,
        IOutboxRepository repository,
        CancellationToken cancellationToken)
    {
        if (dispatchedIds.Count == 0) return;

        // Avoid LINQ .Where() allocation: build the dispatched-messages list manually.
        // In the common case (all success) dispatchedIds.Count == batch.Count — fast path.
        if (dispatchedIds.Count == batch.Count)
        {
            // All messages dispatched — pass the full batch directly.
            await ExecuteDbWithRetryAsync(ct => repository.MarkAsDispatchedAsync(batch, ct), cancellationToken).ConfigureAwait(false);
        }
        else
        {
            // Mixed results — filter to dispatched-only without LINQ.
            var dispatchedMessages = new List<OutboxMessage>(dispatchedIds.Count);
            foreach (var m in batch)
            {
                if (dispatchedIds.Contains(m.Id))
                    dispatchedMessages.Add(m);
            }
            await ExecuteDbWithRetryAsync(ct => repository.MarkAsDispatchedAsync(dispatchedMessages, ct), cancellationToken).ConfigureAwait(false);
        }
    }


    internal static OutboxMessageMetadata BuildMetadata(OutboxMessage message, Dictionary<string, string>? headers)
    {
        MetadataEntry[]? entries = null;


        if (headers is { Count: > 0 })
        {
            entries = new MetadataEntry[headers.Count];
            int i = 0;
            foreach (var kv in headers)
                entries[i++] = new MetadataEntry(kv.Key, kv.Value);
        }

        return new OutboxMessageMetadata(
            correlationId: message.CorrelationId,
            causationId: message.CausationId,
            messageType: message.MessageType,
            entries: entries);
    }

    /// <summary>
    /// Returns a <see cref="System.Diagnostics.TagList"/> with the <c>message_type</c> dimension,
    /// or an empty tag list if <see cref="OutboxRuntimeOptions.IncludeMessageTypeTag"/> is <c>false</c>.
    /// </summary>
    /// <remarks>
    /// AUDIT-FIX G2: Centralizes the IncludeMessageTypeTag check to a single call site.
    /// A stack-allocated TagList (struct) with zero or one element incurs zero heap allocation.
    /// </remarks>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private System.Diagnostics.TagList MessageTypeTag(string? messageType)
    {
        if (!_includeMessageTypeTag || messageType is null)
            return default;
        var tags = new System.Diagnostics.TagList();
        tags.Add("message_type", messageType);
        return tags;
    }

    private async ValueTask ExecuteDbWithRetryAsync(Func<CancellationToken, ValueTask> operation, CancellationToken cancellationToken)
    {
        // G2.2-FIX: Use configurable retry parameters from OutboxDispatcherOptions
        // instead of hardcoded values. This allows tuning for high-latency DB environments.
        //
        // ISSUE-4 FIX: Exponential backoff with ±25% jitter replaces the previous linear
        // backoff (baseDelayMs * attempt). Rationale:
        // - Exponential growth (2^(attempt-1)) spreads retries over a wider window.
        // - ±25% jitter prevents synchronized storm recovery when N concurrent consumers
        //   all fail simultaneously and retry at exactly the same linear intervals — a
        //   classic thundering herd scenario during transient DB blips.
        // - The exponent is capped at 10 (max 1024x base) to bound the upper delay.
        int maxAttempts = _options.DbRetryMaxAttempts;
        int baseDelayMs = _options.DbRetryBaseDelayMs;
        int attempt = 0;
        while (true)
        {
            try
            {
                await operation(cancellationToken).ConfigureAwait(false);
                return;
            }

            // Stryker disable all : Retry limit check per ADR-013
            catch (Exception ex) when (ex is not OperationCanceledException && attempt < maxAttempts)
            // Stryker restore all
            {
                attempt++;
                _logger.DbRetryAttempt(ex, attempt, maxAttempts);

                var delay = CalculateBackoffDelay(attempt, baseDelayMs);
                await Task.Delay(delay, _timeProvider, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    internal static TimeSpan CalculateBackoffDelay(int attempt, int baseDelayMs, Func<double>? randomProvider = null)
    {
        var exponentialMs = (double)baseDelayMs * (1 << Math.Min(attempt - 1, 10));
        var rand = randomProvider != null ? randomProvider() : Random.Shared.NextDouble();
        var jitterMs = (int)(exponentialMs * 0.25 * (2.0 * rand - 1.0));
        var delayMs = (int)Math.Max(1, exponentialMs + jitterMs);
        return TimeSpan.FromMilliseconds(delayMs);
    }

    internal static BoundedChannelOptions CreateChannelOptions(OutboxDispatcherOptions options)
    {
        return new BoundedChannelOptions(options.ChannelCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleWriter = true,
            SingleReader = options.MaxDegreeOfParallelism == 1
        };
    }

    private bool TryDeserializeHeaders(
        OutboxMessage message,
        HeadersDeserializationCache cache,
        out Dictionary<string, string>? headers,
        out string? parentTraceId,
        out string? parentTraceState,
        out DispatchResult result)
    {
        headers = null;
        parentTraceId = null;
        parentTraceState = null;
        result = default;

        try
        {
            if (message.Payload.Length > _baseOptions.MaxPayloadSizeInBytes)
            {
                _logger.PayloadTooLarge(message.Id, message.Payload.Length);
                result = DispatchResult.FailFatal(new OutboxPayloadTooLargeException(message.Payload.Length, _baseOptions.MaxPayloadSizeInBytes));
                return false;
            }
            if (!message.Headers.IsEmpty)
            {
                if (message.Headers.Length > _baseOptions.MaxHeaderSizeInBytes)
                {
                    _logger.HeadersTooLarge(message.Id, message.Headers.Length);
                    result = DispatchResult.FailFatal(new OutboxHeadersTooLargeException(message.Headers.Length, _baseOptions.MaxHeaderSizeInBytes));
                    return false;
                }

                if (cache.LastHeadersMemory.HasValue &&
                    message.Headers.Span.SequenceEqual(cache.LastHeadersMemory.Value.Span))
                {
                    headers = cache.LastHeadersDict;
                }
                else
                {
                    cache.CurrentHeaders.Clear();
                    headers = cache.CurrentHeaders;
                    ParseHeadersFast(message.Headers.Span, headers);
                    // Swap dictionaries to avoid allocating a new one on every unique header.
                    // The dictionary we just filled becomes the 'last' one.
                    // The previous 'last' one is cleared and reused as the next 'current'.
                    cache.Swap(message.Headers, headers);
                    headers = cache.LastHeadersDict;
                }

                // Stryker disable all 
                headers!.TryGetValue("traceparent", out parentTraceId);
                headers.TryGetValue("tracestate", out parentTraceState);
                // Stryker restore all
            }
            return true;
        }
        catch (Exception ex)
        {
            _logger.HeadersDeserializeFailed(ex, message.Id);
            result = DispatchResult.FailFatal(new InvalidOperationException("Failed to deserialize headers", ex));
            return false;
        }
    }

    private async Task HandleFailureAsync(
        OutboxMessage message,
        DispatchResult result,
        IOutboxRepository repository,
        IDeadLetterRepository? dlqRepository,
        CancellationToken cancellationToken)
    {
        bool isMaxRetriesReached = (message.RetryCount + 1) >= _options.MaxRetryCount;
        bool shouldDeadLetter = !result.ShouldRetry || isMaxRetriesReached;

        if (!shouldDeadLetter)
        {
            if (!result.IncrementRetryCount)
            {
                _logger.MessageDelayedNoRetry(message.Id);
                return;
            }


            _metrics.RetryAttemptsTotal.Add(1, MessageTypeTag(message.MessageType));
            var sanitizedError = _errorSanitizer.Sanitize(result.Error!);
            await ExecuteDbWithRetryAsync(ct => repository.MarkAsFailedAsync(message, sanitizedError, isDeadLetter: false, ct), cancellationToken).ConfigureAwait(false);
            return;
        }


        _metrics.DeadLettersTotal.Add(1, MessageTypeTag(message.MessageType));

        // P0-FIX: isDeadLetterFinal is always true now.
        //
        // Previous behaviour (isDeadLetterFinal = false on DLQ INSERT failure) caused an infinite loop:
        // the message was left in state=3 (Failed) with retry_count >= MaxRetryCount, but the poller
        // continued to re-fetch it (state=3 is included in WHERE state IN (0,3)) and the dispatcher
        // continued to dead-letter it indefinitely.
        //
        // Correct behaviour: always mark the outbox row as state=4 (DeadLettered) regardless of whether
        // the DLQ INSERT succeeds. If the DLQ INSERT fails, the dead-letter record is lost from the DLQ
        // table, but the message will no longer be re-fetched. Ops are alerted via the DlqInsertFailed
        // log (level=Error) which includes the message ID so the record can be manually recreated.
        const bool isDeadLetterFinal = true;
        if (dlqRepository != null)
        {
            var deadLetterMsg = DeadLetterMessage.FromOutboxMessage(
                message,
                retryCount: message.RetryCount,
                reason: GetDlqReason(result),
                lastError: _errorSanitizer.Sanitize(result.Error!));
            try
            {
                await ExecuteDbWithRetryAsync(ct => dlqRepository.InsertAsync(deadLetterMsg, default, ct), cancellationToken).ConfigureAwait(false);
                _logger.MessageDeadLettered(message.Id, message.MessageType, message.RetryCount);
            }
            catch (Exception ex)
            {
                // DLQ INSERT failed — message will still be marked state=4 in the outbox to prevent
                // infinite reprocessing. The DLQ entry is lost; the log below (level=Error) must be
                // monitored so that ops can manually recreate the DLQ record if needed.
                _logger.DlqInsertFailed(ex, message.Id, message.MessageType);
                // P2-B FIX: Emit a dedicated counter so ops dashboards can alert on DLQ INSERT failures
                // without relying solely on log scraping. Tag with message_type for root-cause isolation.

                _metrics.DlqInsertFailures.Add(1, new System.Diagnostics.TagList { { "message_type", message.MessageType } });

                // F-04 AUDIT FIX: Emit the full message payload as a structured log fallback so that
                // log aggregators (Seq, Loki, Elasticsearch, Azure Monitor) can act as a DLQ fallback.
                // This allows operators to replay dead-lettered messages even when the DLQ table is
                // unavailable (full, misconfigured, or schema error).
                //
                // Guard: Only emit if payload fits within MaxPayloadSizeInBytes to prevent a single
                // large message from flooding the log pipeline.
                //
                // Security note: The payload is written to logs verbatim. Ensure your log aggregator
                // applies appropriate access controls and retention policies if payloads contain PII.

                if (message.Payload.Length <= _baseOptions.MaxPayloadSizeInBytes)
                {
                    var payloadJson = System.Text.Encoding.UTF8.GetString(message.Payload.Span);
                    var dlqReason = GetDlqReason(result);
                    _logger.DlqPayloadFallback(
                        message.Id,
                        message.MessageType,
                        message.RetryCount,
                        dlqReason,
                        payloadJson);
                }
            }
        }

        await ExecuteDbWithRetryAsync(ct => repository.MarkAsFailedAsync(
            message,
            _errorSanitizer.Sanitize(result.Error!),
            isDeadLetter: isDeadLetterFinal,
            ct), cancellationToken).ConfigureAwait(false);
    }




    internal static void ParseHeadersFast(ReadOnlySpan<byte> span, Dictionary<string, string> headers)
    {
        if (span.IsEmpty) return;
        var reader = new System.Text.Json.Utf8JsonReader(span);
        reader.Read();
        if (reader.TokenType != System.Text.Json.JsonTokenType.StartObject)
        {
            return;
        }

        while (reader.Read())
        {
            if (reader.TokenType == System.Text.Json.JsonTokenType.EndObject)
            {
                break;
            }

            var key = reader.GetString();
            reader.Read();
            if (reader.TokenType == System.Text.Json.JsonTokenType.String)
            {
                var value = reader.GetString();
                headers[key!] = value!;
            }
            else if (reader.TokenType is System.Text.Json.JsonTokenType.StartObject or System.Text.Json.JsonTokenType.StartArray)
            {
                reader.Skip();
            }
        }
    }

    internal void FillBatchFast(List<OutboxMessage> batch, long startTicks)
    {
        while (batch.Count < 100)
        {
            if (_channel.Reader.TryRead(out var msg))
            {
                batch.Add(msg);
                if (Environment.TickCount64 - startTicks >= 50)
                    break;
            }
            else
            {
                break;
            }
        }
    }

    private void RecordDispatchFailureMetrics(DispatchResult result, OutboxMessage message)
    {
        if (_includeMessageTypeTag)
        {
            _metrics.DispatchFailures.Add(1,
                new System.Diagnostics.TagList
                {
                    { "error.type", result.ShouldRetry ? "transient" : "fatal" },
                    // Stryker disable once all 
                    { "message_type", message.MessageType }
                });
        }
        else
        {
            _metrics.DispatchFailures.Add(1,
                new System.Diagnostics.TagList
                {
                    { "error.type", result.ShouldRetry ? "transient" : "fatal" }
                });
        }
    }

    private void RecordProcessMetrics(OutboxMessage message, bool skipExecution, long swTicks)
    {
        var elapsed = skipExecution ? default : System.Diagnostics.Stopwatch.GetElapsedTime(swTicks);
        var elapsedSecs = elapsed.TotalSeconds;

        // Stryker disable once all 
        _metrics.DispatchDuration.Record(
            elapsedSecs,
            MessageTypeTag(message.MessageType));

        // Stryker disable once all 
        _metrics.QueueDuration.Record(
            (DateTimeOffset.UtcNow - message.CreatedAt).TotalSeconds,
            MessageTypeTag(message.MessageType));
    }

    private void LogMessageDispatched(OutboxMessage message, long swTicks)
    {
        var elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(swTicks);
        _logger.MessageDispatched(message.Id, message.MessageType, (long)elapsed.TotalMilliseconds);
    }

    private void LogMessageDispatchFailed(OutboxMessage message, DispatchResult result)
    {
        // Stryker disable once all 
        _logger.MessageDispatchFailed(result.Error!, message.Id, message.MessageType);
    }
}






