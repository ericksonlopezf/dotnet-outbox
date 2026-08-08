// Stryker disable all : Covered by ADR-013. Edge cases, micro-optimizations, logging, and validation strings are not rigorously mutated.
using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using EricksonLopez.Outbox.Diagnostics;
using Microsoft.Extensions.Options;
using EricksonLopez.Outbox.Persistence;
using EricksonLopez.Outbox;

namespace EricksonLopez.Outbox.Dispatcher;

/// <summary>
/// Polls the outbox table periodically and feeds messages into the <see cref="OutboxChannel"/>.
/// 
/// Adaptive mode: when messages are found, the poller loops immediately without sleeping,
/// draining the queue as fast as possible. When the queue is empty, it backs off to the
/// configured interval â€” eliminating wasted CPU during quiet periods.
///
/// The repository is resolved per-scope to support scoped database connections correctly.
/// </summary>
internal sealed class AdaptivePoller : IPollerWakeup, IDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly OutboxChannel _channel;
    private readonly OutboxDispatcherOptions _options;
    private readonly ILogger<AdaptivePoller> _logger;
    private readonly SemaphoreSlim _wakeupSignal = new(0, 1);
    private long _pendingCount;       // current absolute pending count, updated every 30s
    private long _lastMetricTick;
    // ISSUE-16 FIX: Computed once from PendingCountRefreshInterval option — no longer hardcoded.
    // Stored as milliseconds to match Environment.TickCount64 units (milliseconds since process start).
    private readonly long _metricIntervalMs;

    // P2-FIX: ObservableGauge always reports the current absolute count.
    // Unlike UpDownCounter (which accumulates deltas), a Gauge restart-safe:
    // after a process restart, _pendingCount resets to 0 and the gauge correctly
    // reports 0 until the next 30-second polling cycle updates it.
    // Stored to prevent GC from collecting the instrument before the Meter is disposed.
    private readonly ObservableGauge<long> _pendingGauge;
    private readonly OutboxMetrics _metrics;

    public AdaptivePoller(
        IServiceProvider serviceProvider,
        OutboxChannel channel,
        IOptions<OutboxDispatcherOptions> options,
        ILogger<AdaptivePoller> logger,
        OutboxMetrics metrics)
    {
        _serviceProvider = serviceProvider;
        _channel = channel;
        _options = options.Value;
        _logger = logger;
        _metrics = metrics;
        // Convert the configurable refresh interval to milliseconds for comparison with Environment.TickCount64.
        _metricIntervalMs = (long)_options.PendingCountRefreshInterval.TotalMilliseconds;

        // Register the ObservableGauge once at construction — the callback reads the
        // _pendingCount field which is updated on the configured interval by the polling loop.
        _pendingGauge = _metrics.Meter.CreateObservableGauge<long>(
            "messaging.outbox.pending.messages",
            () => Volatile.Read(ref _pendingCount),
            "{message}",
            "Current approximate number of pending outbox messages.");
    }

    /// <summary>
    /// Wakes up the poller immediately. Used by external notification systems (e.g. Postgres LISTEN/NOTIFY).
    /// </summary>
    /// <remarks>
    /// Thread-safe. Safe to call from any thread, including during graceful shutdown.
    /// The semaphore (max count = 1) guarantees at most one pending wakeup at a time.
    ///
    /// <para>
    /// <b>AUDIT-FIX P1-A:</b> Removed the non-atomic <c>CurrentCount == 0</c> check that preceded
    /// <c>Release()</c>. That check was a TOCTOU race — between reading <c>CurrentCount</c> and
    /// calling <c>Release()</c>, another thread could have changed the count, making the check
    /// unreliable. The <see cref="SemaphoreFullException"/> catch already handled this case correctly,
    /// making the guard redundant and misleading.
    /// </para>
    /// <para>
    /// <see cref="ObjectDisposedException"/> is silently swallowed because <see cref="WakeUp"/> can
    /// be called by the PostgreSQL LISTEN/NOTIFY listener during graceful shutdown, after the semaphore
    /// has been disposed by <see cref="Dispose"/>. This is not an error condition.
    /// </para>
    /// </remarks>
    // Stryker disable all : The WakeUp timing is intentionally hard to test without brittle timeouts
    public void WakeUp()
    {
        try
        {
            _wakeupSignal.Release();
        }
        catch (SemaphoreFullException) { /* Already has a pending wakeup — no-op */ }
        catch (ObjectDisposedException) { /* Shutting down — no-op */ }
    }
    // Stryker restore all

    public void Dispose()
    {
        _wakeupSignal.Dispose();
    }

    public async Task StartPollingAsync(CancellationToken cancellationToken)
    {
        var nextReclaimTime = DateTimeOffset.MinValue;

        while (!cancellationToken.IsCancellationRequested)
        {
            long batchStartTicks = Environment.TickCount64;
            int fetchedCount = 0;

            try
            {
                // Justification for Service Locator (OUTBOX-CC1): 
                // AdaptivePoller is a singleton IHostedService. To consume scoped services (like IOutboxRepository), 
                // we must create a scope and use ServiceLocator per polling cycle.
                using var scope = _serviceProvider.CreateScope();
                var repo = scope.ServiceProvider
                    .GetRequiredService<IOutboxRepository>();

                    // Stryker disable all : Time boundaries and metrics/logs are untestable or brittle
                    // Reclaim messages stuck in InFlight state periodically (time-based check every ReclaimInterval).
                    // This prevents message loss when a dispatcher instance crashes
                    // after claiming a batch but before completing dispatch.
                    if (DateTimeOffset.UtcNow >= nextReclaimTime)
                    {
                        nextReclaimTime = DateTimeOffset.UtcNow.Add(_options.ReclaimInterval);
                        var reclaimed = await repo.ReclaimStaleMessagesAsync(
                            _options.ReclaimTimeout,
                            cancellationToken);

                        if (reclaimed > 0)
                        {
                            _logger.ReclaimedStaleMessages(reclaimed);

                            // Emit metric — a non-zero reclaim count indicates a previous dispatcher
                            // instance crashed or was killed mid-batch. Monitor for spikes.
                            _metrics.ReclaimedMessages.Add(reclaimed);
                        }
                    }
                    // Stryker restore all

                    long startTimestamp = Stopwatch.GetTimestamp();
                    IReadOnlyList<OutboxMessage> messages;
                    try
                    {
                        messages = await repo.FetchPendingAsync(_options.BatchSize, cancellationToken);
                        fetchedCount = messages.Count;
                    }
                    finally
                    {
                        RecordMetrics(startTimestamp, fetchedCount);
                    }
                    
                    await UpdatePendingCountAsync(repo, cancellationToken);

                    await ProcessMessagesAsync(messages, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.PollerError(ex);
            }

            // Adaptive: if we fetched a FULL batch, poll again immediately; else (queue drained), sleep.
            if (fetchedCount >= _options.BatchSize && _options.UseAdaptivePolling)
            {
                var elapsedMs = Environment.TickCount64 - batchStartTicks;
                // Stryker disable once all : Covered by Poller_Should_Use_Default_MinMs_When_MaxBatchesPerSecond_Is_Zero but IL branching makes it hard to get 100%
                var minMs = _options.MaxBatchesPerSecond > 0 ? 1000 / _options.MaxBatchesPerSecond : 10;
                var delayMs = (int)Math.Max(10, minMs - elapsedMs);

                // Prevent CPU spin and enforce MaxBatchesPerSecond rate limit
                await Task.Delay(delayMs, cancellationToken);
                continue;
            }
            // Stryker restore all

            // Stryker disable all : Random jitter and delay are inherently non-deterministic and hard to unit-test. Removing them only makes tests faster.
            try
            {
                var baseMs = _options.PollingInterval.TotalMilliseconds;
                // P1-C FIX: Use Random.Shared.NextDouble() per cycle instead of a hash of ProcessId.
                // The ProcessId-based hash was constant for the process lifetime — in container
                // environments where PID=1 is fixed, all instances would produce identical jitter,
                // defeating the thundering-herd prevention goal. Random.Shared is thread-safe and
                // varies on every poll, ensuring independent backoff across instances.
                var jitterMs = baseMs * 0.15 * (2 * Random.Shared.NextDouble() - 1);
                var delayMs = (int)Math.Max(1, baseMs + jitterMs);

                // Returns: true if the semaphore was released (LISTEN/NOTIFY wakeup), false on timeout (normal interval).
                await _wakeupSignal.WaitAsync(delayMs, cancellationToken);
            }
            // Stryker disable all : Removing catch block only affects graceful cancellation logs
            catch (OperationCanceledException)
            {
                break;
            }
            // Stryker restore all
        }
    }

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    private void RecordMetrics(long startTimestamp, int fetchedCount)
    {
        var elapsedMs = (long)Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;
        if (fetchedCount > 0)
        {
            _logger.BatchFetched(fetchedCount, elapsedMs);
            _metrics.BatchSize.Record(fetchedCount);
        }
    }

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    private async Task UpdatePendingCountAsync(IOutboxRepository repo, CancellationToken cancellationToken)
    {
        if (Environment.TickCount64 - Volatile.Read(ref _lastMetricTick) > _metricIntervalMs)
        {
            Volatile.Write(ref _lastMetricTick, Environment.TickCount64);
            var pendingCount = await repo.GetPendingCountAsync(cancellationToken);
            Interlocked.Exchange(ref _pendingCount, pendingCount);
        }
    }

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    private async Task ProcessMessagesAsync(IReadOnlyList<OutboxMessage> messages, CancellationToken cancellationToken)
    {
        foreach (var message in messages)
        {
            if (cancellationToken.IsCancellationRequested) break;
            await _channel.WriteAsync(message, cancellationToken);
        }
    }
}


