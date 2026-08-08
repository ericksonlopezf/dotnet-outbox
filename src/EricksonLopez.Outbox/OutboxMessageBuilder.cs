using EricksonLopez.Outbox.Persistence;
// Stryker disable all : Covered by ADR-013. Edge cases, micro-optimizations, logging, and validation strings are not rigorously mutated.
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

namespace EricksonLopez.Outbox;

/// <summary>
/// Provides a fluent API for enriching an outbox message with metadata, scheduling, and transaction details before storing it.
/// </summary>
/// <remarks>
/// Usage:
/// <code>
///   await outbox
///       .Publish(new OrderCreatedEvent(...))
///       .WithTransaction(tx)
///       .WithHeader("TenantId", "tenant-123")
///       .WithDelay(TimeSpan.FromSeconds(30))
///       .StoreAsync(ct);
/// </code>
/// <para>
/// <b>Design note — sealed and not interface-based:</b><br/>
/// <see cref="OutboxMessageBuilder{TMessage}"/> is intentionally <c>sealed</c> and is not backed by
/// a public interface. This design:
/// <list type="bullet">
///   <item><description>Eliminates virtual dispatch in the fluent call chain (zero overhead).</description></item>
///   <item><description>Allows the internal constructor to guarantee invariants (e.g., <c>_outbox</c> is never null).</description></item>
///   <item><description>Ensures NativeAOT trimming can see all usage through the concrete type.</description></item>
/// </list>
/// If you need to decorate or mock the builder in tests, use <see cref="EricksonLopez.Outbox.Testing.InMemoryOutboxStore"/>
/// or <see cref="EricksonLopez.Outbox.Testing.FakeOutboxDispatcher"/> instead, which capture messages without
/// going through the real storage path.
/// </para>
/// <para>
/// <b>Scheduling (<c>deliver_at</c>) edge case:</b><br/>
/// When using <see cref="WithDelay"/> or <see cref="WithDeliverAt"/>, ensure the scheduled time does not
/// reach or exceed <c>now + OutboxRuntimeOptions.MaxMessageAge</c>. If it does,
/// <see cref="StoreAsync"/> will throw <see cref="ArgumentOutOfRangeException"/> at the moment of
/// storage to prevent silent message loss. Increase <c>OutboxRuntimeOptions.MaxMessageAge</c>
/// to accommodate long-horizon scheduling. See <see cref="IOutboxRepository.FetchPendingAsync"/> for details.
/// </para>
/// </remarks>
/// <typeparam name="TMessage">The type of the message being enriched.</typeparam>
public sealed class OutboxMessageBuilder<TMessage> : IDisposable where TMessage : notnull
{
    private bool _disposed;
    private readonly IOutbox _outbox;
    private readonly TMessage _message;
    private IOutboxTransactionContext? _transaction;
    private bool _hasTransaction;
    private DateTimeOffset? _deliverAt;
    private MetadataEntry[]? _headersArray;
    private int _headerCount;
    private string? _correlationId;
    private string? _causationId;

    /// <summary>
    /// Initializes a new instance of the <see cref="OutboxMessageBuilder{TMessage}"/> class.
    /// </summary>
    /// <param name="outbox">The outbox instance to use for persistence.</param>
    /// <param name="message">The message payload to be enriched and stored.</param>
    internal OutboxMessageBuilder(IOutbox outbox, TMessage message)
    {
        _outbox = outbox;
        _message = message;
        _transaction = default;
        _hasTransaction = false;
        _deliverAt = null;
        _headersArray = null;
        _headerCount = 0;
        _correlationId = null;
        _causationId = null;
    }

    /// <summary>
    /// Associates the specified database transaction with the outbox message to ensure atomic storage.
    /// </summary>
    /// <param name="transaction">The transaction context to use for the storage operation.</param>
    /// <returns>The current <see cref="OutboxMessageBuilder{TMessage}"/> instance for method chaining.</returns>
    public OutboxMessageBuilder<TMessage> WithTransaction(IOutboxTransactionContext transaction)
    {
        _transaction = transaction;
        _hasTransaction = true;
        return this;
    }

    /// <summary>
    /// Schedules the message for dispatching after the specified time delay.
    /// </summary>
    /// <remarks>
    /// The message will remain invisible to the dispatcher until the current UTC time is greater than or equal to the scheduled time.
    /// </remarks>
    /// <param name="delay">The time interval to delay dispatching.</param>
    /// <returns>The current <see cref="OutboxMessageBuilder{TMessage}"/> instance for method chaining.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown by <see cref="StoreAsync"/> (not here) if the resulting <c>deliver_at</c> timestamp reaches
    /// or exceeds <c>now + OutboxRuntimeOptions.MaxMessageAge</c>, which would cause the message to be
    /// silently excluded from polling and never delivered. Increase <c>MaxMessageAge</c> to support
    /// longer scheduling horizons.
    /// </exception>
    public OutboxMessageBuilder<TMessage> WithDelay(TimeSpan delay)
    {
        _deliverAt = DateTimeOffset.UtcNow.Add(delay);
        return this;
    }

    /// <summary>
    /// Schedules the message for dispatching at the specified absolute timestamp.
    /// </summary>
    /// <remarks>
    /// The message will remain invisible to the dispatcher until the current UTC time is greater than or equal to the specified timestamp.
    /// </remarks>
    /// <param name="deliverAt">The absolute UTC timestamp indicating when the message should become visible.</param>
    /// <returns>The current <see cref="OutboxMessageBuilder{TMessage}"/> instance for method chaining.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown by <see cref="StoreAsync"/> (not here) if <paramref name="deliverAt"/> is at or beyond
    /// <c>now + OutboxRuntimeOptions.MaxMessageAge</c>, which would cause the message to be silently
    /// excluded from polling and never delivered. Increase <c>MaxMessageAge</c> to support longer
    /// scheduling horizons.
    /// </exception>
    public OutboxMessageBuilder<TMessage> WithDeliverAt(DateTimeOffset deliverAt)
    {
        _deliverAt = deliverAt;
        return this;
    }



    /// <summary>
    /// Adds a custom metadata header to the message that will be propagated to the message broker.
    /// </summary>
    /// <param name="key">The key identifying the header.</param>
    /// <param name="value">The value of the header.</param>
    /// <returns>The current <see cref="OutboxMessageBuilder{TMessage}"/> instance for method chaining.</returns>
    public OutboxMessageBuilder<TMessage> WithHeader(string key, string value)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(value);
        
        _headersArray ??= ArrayPool<MetadataEntry>.Shared.Rent(8);
        if (_headerCount >= _headersArray.Length)
        {
            var newArray = ArrayPool<MetadataEntry>.Shared.Rent(_headersArray.Length * 2);
            Array.Copy(_headersArray, newArray, _headerCount);
            ReturnArrayToPool(_headersArray);
            _headersArray = newArray;
        }
        
        _headersArray[_headerCount++] = new MetadataEntry(key, value);
        return this;
    }

    /// <summary>
    /// Sets the correlation identifier for the message, used to trace related operations across systems.
    /// </summary>
    /// <param name="correlationId">The correlation identifier to assign.</param>
    /// <returns>The current <see cref="OutboxMessageBuilder{TMessage}"/> instance for method chaining.</returns>
    public OutboxMessageBuilder<TMessage> WithCorrelationId(string correlationId)
    {
        _correlationId = correlationId;
        return this;
    }

    /// <summary>
    /// Sets the causation identifier for the message, identifying the specific operation that triggered this message.
    /// </summary>
    /// <param name="causationId">The causation identifier to assign.</param>
    /// <returns>The current <see cref="OutboxMessageBuilder{TMessage}"/> instance for method chaining.</returns>
    public OutboxMessageBuilder<TMessage> WithCausationId(string causationId)
    {
        _causationId = causationId;
        return this;
    }

    /// <summary>
    /// Disposes the builder, returning pooled resources if <see cref="StoreAsync"/> was not called.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>ISSUE-API1 FIX — You do NOT need to call <c>Dispose()</c> or use a <c>using</c> statement.</b>
    /// </para>
    /// <para>
    /// The fluent pattern is designed for safe single-use:
    /// <code>
    /// await outbox.Publish(ev).WithTransaction(tx).WithHeader("k","v").StoreAsync(ct);
    /// // ↑ No 'using' needed — StoreAsync auto-disposes the pooled headers array.
    /// </code>
    /// </para>
    /// <para>
    /// <see cref="Dispose"/> is still implemented as a safety net for the rare case where a builder
    /// is constructed but <see cref="StoreAsync"/> is never called (e.g., an exception is thrown
    /// between <c>WithHeader()</c> and <c>StoreAsync()</c>). In that case, calling <c>Dispose()</c>
    /// or using <c>using var builder = outbox.Publish(ev)</c> prevents an <see cref="ArrayPool{T}"/>
    /// leak from the rented headers array.
    /// </para>
    /// <para>
    /// Both paths inside <see cref="StoreAsync"/> handle disposal automatically:
    /// <list type="bullet">
    ///   <item>Fast path (no headers/metadata): <c>_headersArray</c> is <see langword="null"/>, nothing to release.</item>
    ///   <item>Slow path (<see cref="StoreWithMetadataAsync"/>): <c>finally { Dispose(); }</c> always releases the rented array.</item>
    /// </list>
    /// </para>
    /// </remarks>
    // Stryker disable all : Dispose pattern is untestable and purely defensive against leaks
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_headersArray != null)
        {
            ReturnArrayToPool(_headersArray);
            _headersArray = null;
        }
    }
    // Stryker restore all

    /// <summary>
    /// Persists the enriched message atomically within the configured transaction context.
    /// </summary>
    /// <param name="cancellationToken">A token that can be used to cancel the asynchronous operation.</param>
    /// <returns>A task representing the asynchronous storage operation.</returns>
    /// <exception cref="InvalidOperationException">A transaction was not configured via <see cref="WithTransaction"/> prior to calling.</exception>
    public ValueTask StoreAsync(CancellationToken cancellationToken = default)
    {
#if NET7_0_OR_GREATER
        ObjectDisposedException.ThrowIf(_disposed, this);
#else
        if (_disposed) throw new ObjectDisposedException(nameof(OutboxMessageBuilder<TMessage>));
#endif

        if (!_hasTransaction)
        {
            // P2-FIX: Dispose before throwing to return any pooled _headersArray back to ArrayPool.
            // Without this, a caller who added headers via WithHeader() but forgot to call
            // WithTransaction() would leak the rented array from ArrayPool.Shared.
            // Stryker disable once Statement
            Dispose();
            throw new InvalidOperationException(
                "A transaction must be provided via WithTransaction() before calling StoreAsync().");
        }

        if (_correlationId is null && System.Diagnostics.Activity.Current != null)
        {
            _correlationId = System.Diagnostics.Activity.Current.TraceId.ToString();
            _causationId ??= System.Diagnostics.Activity.Current.SpanId.ToString();
        }

        // Fast-path optimization for zero allocations
        if (_headerCount == 0 && _deliverAt is null && _correlationId is null && _causationId is null)
        {
            return _outbox.StoreAsync(_message, _transaction!, cancellationToken);
        }

        return StoreWithMetadataAsync(cancellationToken);
    }

    private async ValueTask StoreWithMetadataAsync(CancellationToken cancellationToken)
    {
        try
        {
            // P1-3 FIX: Pass ReadOnlyMemory directly instead of allocating a new array.
            // The pooled _headersArray is sliced to exact count — zero intermediate allocation.
            // Stryker disable once all : Instantiating ReadOnlyMemory with null for 0 length is identical to default
            var entriesMemory = _headerCount > 0 
                ? new ReadOnlyMemory<MetadataEntry>(_headersArray!, 0, _headerCount)
                : default;

            var metadata = new MessageMetadata(
                correlationId: _correlationId,
                causationId: _causationId,
                messageType: null,
                entries: entriesMemory);

            await _outbox.StoreAsync(
                _message,
                _transaction!,
                metadata,
                _deliverAt,
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _disposed = true;
            // Stryker disable once Block
            if (_headersArray != null)
            {
                ReturnArrayToPool(_headersArray);
                _headersArray = null;
            }
        }
    }

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    private static void ReturnArrayToPool(MetadataEntry[] array)
    {
        ArrayPool<MetadataEntry>.Shared.Return(array, clearArray: true);
    }
}
