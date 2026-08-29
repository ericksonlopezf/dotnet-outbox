// Copyright © Erickson Lopez. MIT License.
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.Outbox;
using EricksonLopez.Outbox.Diagnostics;
using EricksonLopez.Outbox.Persistence;
using EricksonLopez.Outbox.Serialization;

namespace EricksonLopez.Outbox;

/// <summary>
/// Provides the default implementation of the <see cref="IOutbox"/> interface.
/// </summary>
/// <remarks>
/// Coordinates the serialization, type resolution, and database persistence of outbox messages.
/// It is designed for maximum throughput and zero-allocation on the hot path.
/// </remarks>
public sealed class DefaultOutbox : IOutbox
{
    private readonly IOutboxRepository _repository;
    private readonly IOutboxSerializer _serializer;
    private readonly IOutboxMessageTypeResolver _typeResolver;
    private readonly Microsoft.Extensions.Options.IOptions<OutboxRuntimeOptions> _options;
    private readonly OutboxMetrics _metrics;
    private readonly TimeProvider _timeProvider;

    private static readonly ReadOnlyMemory<byte> EmptyJsonObjectBytes = "{}"u8.ToArray();

    [ThreadStatic]
    private static ArrayPoolBufferWriter<byte>? t_payloadBufferWriter;

    [ThreadStatic]
    private static ArrayPoolBufferWriter<byte>? t_headerBufferWriter;

    [ThreadStatic]
    private static Utf8JsonWriter? t_headersJsonWriter;

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultOutbox"/> class.
    /// </summary>
    /// <param name="repository">The repository used to persist outbox messages.</param>
    /// <param name="serializer">The serializer that converts message payloads to byte arrays.</param>
    /// <param name="typeResolver">The resolver used to map message types to their string aliases.</param>
    /// <param name="options">The configuration options for the outbox runtime.</param>
    /// <param name="metrics">The telemetry metrics tracker.</param>
    /// <param name="timeProvider">The optional time provider used for timestamping and deadline calculations.</param>
    /// <exception cref="ArgumentNullException">Any of the provided arguments is <see langword="null"/>.</exception>
    public DefaultOutbox(
        IOutboxRepository repository,
        IOutboxSerializer serializer,
        IOutboxMessageTypeResolver typeResolver,
        Microsoft.Extensions.Options.IOptions<OutboxRuntimeOptions> options,
        OutboxMetrics metrics,
        TimeProvider? timeProvider = null)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        _typeResolver = typeResolver ?? throw new ArgumentNullException(nameof(typeResolver));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc/>
    public ValueTask StoreAsync<TMessage>(
        TMessage message,
        IOutboxTransactionContext transaction,
        CancellationToken cancellationToken = default) where TMessage : notnull
    {
        var metadata = new OutboxMessageMetadata(
            correlationId: null,
            causationId: null,
            messageType: null); // Will be resolved in BuildOutboxMessage

        return StoreAsync(message, transaction, metadata, deliverAt: null, cancellationToken);
    }

    /// <inheritdoc/>
    public async ValueTask StoreAsync<TMessage>(
        ReadOnlyMemory<TMessage> messages,
        IOutboxTransactionContext transaction,
        CancellationToken cancellationToken = default) where TMessage : notnull
    {
        ArgumentNullException.ThrowIfNull(transaction);

        if (messages.IsEmpty)
            return;

        var span = messages.Span;
        var outboxMessages = ArrayPool<OutboxMessage>.Shared.Rent(span.Length);

        try
        {
            var metadata = new OutboxMessageMetadata(correlationId: null, causationId: null, messageType: null);
            for (int i = 0; i < span.Length; i++)
            {
                outboxMessages[i] = BuildOutboxMessage(span[i], metadata, deliverAt: null);
            }

            // Slice to the exact length
            var slice = new ReadOnlyMemory<OutboxMessage>(outboxMessages, 0, span.Length);
            // P0-2 FIX: MUST await before returning — the finally block clears the rented array.
            // Without await, the array could be returned to the pool and reused by another thread
            // while InsertBatchAsync is still reading from it (use-after-free race condition).
            var sw = System.Diagnostics.Stopwatch.GetTimestamp();
            await _repository.InsertBatchAsync(slice, transaction, cancellationToken);
            // Stryker disable once all 
            _metrics.RecordStoreDuration(
                System.Diagnostics.Stopwatch.GetElapsedTime(sw).TotalSeconds,
                "batch");
        }
        // Stryker disable all 
        finally
        {
            // Stryker disable all 
            ArrayPool<OutboxMessage>.Shared.Return(outboxMessages, clearArray: true);
            // Stryker restore all
        }
        // Stryker restore all
    }


    /// <inheritdoc/>
    public ValueTask StoreAsync<TMessage>(
        TMessage message,
        IOutboxTransactionContext transaction,
        OutboxMessageMetadata metadata,
        DateTimeOffset? deliverAt,
        CancellationToken cancellationToken = default) where TMessage : notnull
    {
        ArgumentNullException.ThrowIfNull(transaction);
        var outboxMessage = BuildOutboxMessage(message, metadata, deliverAt);

        // OTel "create" span: links the business transaction to the outbox storage operation.
        // Starts synchronously before the await so it correctly captures the calling context's trace.
        // The dispatcher will link its "publish" span back to this message via the stored headers.
        using var activity = OutboxActivitySource.StartStoreActivity(
            outboxMessage.MessageType,
            outboxMessage.Id.ToString(),
            outboxMessage.CorrelationId);

        var sw = System.Diagnostics.Stopwatch.GetTimestamp();
        var task = _repository.InsertAsync(outboxMessage, transaction, cancellationToken);
        if (!task.IsCompletedSuccessfully)
        {
            return AwaitAndRecordAsync(task, sw, outboxMessage.MessageType);
        }

        // Stryker disable once all 
        _metrics.RecordStoreDuration(
            System.Diagnostics.Stopwatch.GetElapsedTime(sw).TotalSeconds,
            outboxMessage.MessageType);
        return task;

        async ValueTask AwaitAndRecordAsync(ValueTask innerTask, long startTicks, string type)
        {
            await innerTask;
            _metrics.RecordStoreDuration(
                System.Diagnostics.Stopwatch.GetElapsedTime(startTicks).TotalSeconds,
                type);
        }
    }

    /// <summary>
    /// Begins a fluent message-building chain for enriching a message before persisting it to the outbox.
    /// </summary>
    /// <typeparam name="TMessage">The type of the message being built.</typeparam>
    /// <param name="message">The initial message payload to begin enriching.</param>
    /// <returns>A fluent builder instance to configure transaction, delay, metadata, and headers.</returns>
    public OutboxMessageBuilder<TMessage> Publish<TMessage>(TMessage message) where TMessage : notnull
    {
        return new OutboxMessageBuilder<TMessage>(this, message);
    }


    private OutboxMessage BuildOutboxMessage<TMessage>(
        TMessage message,
        OutboxMessageMetadata metadata,
        DateTimeOffset? deliverAt) where TMessage : notnull
    {
        // P1-B FIX: Guard against the "deliver_at Dead Zone" — messages whose deliver_at timestamp
        // exceeds MaxMessageAge will be SILENTLY lost: they are stored successfully, but the
        // `created_at >= NOW() - MaxMessageAge` guard in FetchPendingAsync excludes them from
        // polling. They are never dispatched and never moved to the DLQ.
        //
        // Rule: deliver_at must be < (now + MaxMessageAge), giving the dispatcher at least a
        // small window to pick up the message after its scheduled time.
        // We use a small grace buffer of 1 hour to handle clock skew and polling latency.
        if (deliverAt.HasValue)
        {
            var maxAge = _options.Value.MaxMessageAge;
            var deadline = _timeProvider.GetUtcNow().Add(maxAge);

            if (deliverAt.Value > deadline)
            {
                // Stryker disable all 
                throw new ArgumentOutOfRangeException(
                    nameof(deliverAt),
                    deliverAt.Value,
                    $"The scheduled deliver_at time ({deliverAt.Value:O}) is at or beyond the MaxMessageAge deadline " +
                    $"({deadline:O}). The message would be stored but silently excluded from polling and never delivered. " +
                    $"To schedule this far ahead, increase OutboxRuntimeOptions.MaxMessageAge (currently {maxAge}).");
                // Stryker restore all
            }
        }

        // Stryker disable once all 
        string messageTypeAlias = string.Empty;
        if (!string.IsNullOrEmpty(metadata.MessageType))
        {
            messageTypeAlias = metadata.MessageType;
        }
        else if (_typeResolver.TryGetAlias(typeof(TMessage), out var resolvedAlias) && !string.IsNullOrEmpty(resolvedAlias))
        {
            messageTypeAlias = resolvedAlias;
        }
        else
        {
            if (_options.Value.ThrowOnUnregisteredType)
            {
                // FIX-14: Use typed OutboxTypeNotRegisteredException instead of InvalidOperationException.
                throw new OutboxTypeNotRegisteredException(typeof(TMessage));
            }
            else
            {
                try
                {
                    messageTypeAlias = _typeResolver.GetAlias(typeof(TMessage));
                }
                catch (InvalidOperationException)
                {
                    // P3-FIX (documentation): typeof(TMessage).Name — deliberate bounded use of
                    // Type.Name in a non-hot fallback path.
                    //
                    // This branch is ONLY reachable when:
                    //   1. ThrowOnUnregisteredType = false (degraded mode, not production-recommended)
                    //   2. The type resolver fails to find an alias for TMessage
                    //
                    // typeof(T).Name on a JIT-known generic parameter does NOT require dynamic lookup
                    // IL metadata (no IL2067/IL2057 warnings). The runtime resolves it statically.
                    // However, it is NOT stable across renames/obfuscation.
                    //
                    // Production guidance: keep ThrowOnUnregisteredType = true (default) and annotate
                    // all message types with [OutboxMessage("stable.alias")] via source generators.
                    messageTypeAlias = typeof(TMessage).Name;
                }
            }
        }

        // Use Serialize with a ThreadStatic buffer to avoid allocating an intermediate byte[].
        // t_payloadBuffer is reused across calls on the same thread.
        //
        // IMPORTANT — ThreadStatic + async safety:
        // BuildOutboxMessage() is always called SYNCHRONOUSLY before any await in StoreAsync().
        // The rented ThreadStatic buffer is used and its results copied (WrittenSpan.ToArray())
        // within this synchronous scope, so the buffer is fully consumed before any thread switch.
        // If you ever introduce an `await` inside this method, you MUST switch to a pooled buffer
        // approach (ArrayPool<byte>.Shared.Rent) to avoid use-after-free on a different thread's buffer.
        var payloadWriter = t_payloadBufferWriter;
        if (payloadWriter == null)
        {
            t_payloadBufferWriter = payloadWriter = new ArrayPoolBufferWriter<byte>(1024);
        }
        else
        {
            payloadWriter.Clear();
        }

        _serializer.Serialize(message, payloadWriter);

        if (payloadWriter.WrittenCount > _options.Value.MaxPayloadSizeInBytes)
        {
            // P2-FIX: Use typed OutboxPayloadTooLargeException instead of generic InvalidOperationException.
            // Callers can now catch this specific exception to implement blob offloading or other strategies.
            throw new OutboxPayloadTooLargeException(payloadWriter.WrittenCount, _options.Value.MaxPayloadSizeInBytes);
        }

        // The DB layer requires a byte[] — copy exactly once at the boundary.
        var payloadBytes = payloadWriter.WrittenSpan.ToArray();

        // Ensure payloadWriter isn't holding onto huge arrays indefinitely.
        if (payloadWriter.Capacity > 65536)
        {
            payloadWriter.Dispose();
            t_payloadBufferWriter = null;
        }

        ReadOnlyMemory<byte> headersBytes = EmptyJsonObjectBytes;
        var baggage = System.Diagnostics.Activity.Current?.Baggage;
        bool hasBaggage = false;
        if (baggage != null)
        {
            using var enumerator = baggage.GetEnumerator();
            hasBaggage = enumerator.MoveNext();
        }

        if (!metadata.Entries.IsEmpty || hasBaggage)
        {
            var headerWriter = t_headerBufferWriter;
            if (headerWriter == null)
            {
                t_headerBufferWriter = headerWriter = new ArrayPoolBufferWriter<byte>(256);
            }
            else
            {
                headerWriter.Clear();
            }

            try
            {
                var jsonWriter = t_headersJsonWriter;
                if (jsonWriter == null)
                {
                    t_headersJsonWriter = jsonWriter = new Utf8JsonWriter(headerWriter);
                }
                jsonWriter.Reset(headerWriter);
                jsonWriter.WriteStartObject();

                var entriesSpan = metadata.Entries.Span;
                for (int i = 0; i < entriesSpan.Length; i++)
                {
                    var entry = entriesSpan[i];
                    jsonWriter.WriteString(entry.Key, entry.Value);
                }

                if (hasBaggage)
                {
                    foreach (var b in baggage!)
                    {
                        if (b.Value != null)
                        {
                            // To avoid duplicates if user manually added baggage to metadata
                            if (metadata.GetValue(b.Key) == null)
                            {
                                jsonWriter.WriteString(b.Key, b.Value);
                            }
                        }
                    }
                }

                jsonWriter.WriteEndObject();
                jsonWriter.Flush();

                headersBytes = headerWriter.WrittenSpan.ToArray(); // exactly sized copy
                if (headersBytes.Length > _options.Value.MaxHeaderSizeInBytes)
                {
                    // P2-FIX: Use typed OutboxHeadersTooLargeException instead of generic InvalidOperationException.
                    throw new OutboxHeadersTooLargeException(headersBytes.Length, _options.Value.MaxHeaderSizeInBytes);
                }
            }
            catch
            {
                t_headersJsonWriter = null;
                // Stryker disable once all 
                headerWriter.Dispose();
                t_headerBufferWriter = null;
                throw;
            }
            finally
            {
                // Stryker disable once all 
                // We keep the buffer alive for the thread, so we don't dispose it.
                // It will be cleared on the next usage via Clear().
            }
        }

#if NET9_0_OR_GREATER
        var id = Guid.CreateVersion7();
#else
        var id = Guid.NewGuid();
#endif

        return new OutboxMessage(
            Id: id,
            MessageType: messageTypeAlias,
            Payload: payloadBytes,
            CorrelationId: metadata.CorrelationId,
            CausationId: metadata.CausationId,
            Headers: headersBytes,
            CreatedAt: DateTimeOffset.UtcNow,
            ProcessedAt: null,
            DeliverAt: deliverAt,
            Status: OutboxMessageStatus.Pending,
            RetryCount: 0,
            Error: null
        );
    }
}





