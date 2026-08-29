// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.Outbox;
using EricksonLopez.Outbox.Diagnostics;
using EricksonLopez.Outbox.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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
    private readonly TimeProvider _timeProvider;
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
        OutboxMetrics metrics,
        TimeProvider timeProvider)
    {
        _serviceProvider = serviceProvider;
        _channel = channel;
        _options = options.Value;
        _logger = logger;
        _metrics = metrics;
        _timeProvider = timeProvider;
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
    public void WakeUp()
    {
        try
        {
            _wakeupSignal.Release();
        }
        catch (SemaphoreFullException) { /* Already has a pending wakeup — no-op */ }
        catch (ObjectDisposedException) { /* Shutting down — no-op */ }
    }

    // Stryker disable all 
    public void Dispose()
    {
        // Stryker disable once all 
        _wakeupSignal.Dispose();
    }
    // Stryker restore all

    public async Task StartPollingAsync(CancellationToken cancellationToken)
    {
        var nextReclaimTime = DateTimeOffset.MinValue;

        // Stryker disable all : Loop termination check per ADR-013
        while (!cancellationToken.IsCancellationRequested)
        // Stryker restore all
        {
            long batchStartTicks = _timeProvider.GetTimestamp();
            int fetchedCount = 0;

            try
            {
                // Justification for Service Locator (OUTBOX-CC1): 
                // AdaptivePoller is a singleton IHostedService. To consume scoped services (like IOutboxRepository), 
                // we must create a scope and use ServiceLocator per polling cycle.
                using var scope = _serviceProvider.CreateScope();
                var repo = scope.ServiceProvider
                    .GetRequiredService<IOutboxRepository>();

                // Reclaim messages stuck in InFlight state periodically (time-based check every ReclaimInterval).
                // This prevents message loss when a dispatcher instance crashes
                // after claiming a batch but before completing dispatch.
                if (_timeProvider.GetUtcNow() >= nextReclaimTime)
                {
                    nextReclaimTime = _timeProvider.GetUtcNow().Add(_options.ReclaimInterval);
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

                long startTimestamp = _timeProvider.GetTimestamp();
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
                var elapsedMs = _timeProvider.GetElapsedTime(batchStartTicks).TotalMilliseconds;
                // Stryker disable once all 
                var minMs = _options.MaxBatchesPerSecond > 0 ? 1000 / _options.MaxBatchesPerSecond : 10;
                var delayMs = (int)Math.Max(10, minMs - elapsedMs);

                // Prevent CPU spin and enforce MaxBatchesPerSecond rate limit
                await Task.Delay(TimeSpan.FromMilliseconds(delayMs), _timeProvider, cancellationToken);
                continue;
            }

            try
            {
                var timeout = CalculatePollingDelay(_options.PollingInterval);

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                var waitTask = _wakeupSignal.WaitAsync(cts.Token);
                var delayTask = Task.Delay(timeout, _timeProvider, cts.Token);

                var completedTask = await Task.WhenAny(waitTask, delayTask);
                // Stryker disable once all 
                cts.Cancel(); // Cancel the other task
                // Stryker disable once all 
                await completedTask;
            }
            catch (OperationCanceledException)
            {
                // Expected when cancellationToken is cancelled during delay/wait
                // Stryker disable all : Cancellation exit per ADR-013
                break;
                // Stryker restore all
            }
        }
    }

    internal static TimeSpan CalculatePollingDelay(TimeSpan pollingInterval, Func<double>? randomProvider = null)
    {
        var baseMs = pollingInterval.TotalMilliseconds;
        var rand = randomProvider != null ? randomProvider() : Random.Shared.NextDouble();
        var jitterMs = baseMs * 0.15 * (2 * rand - 1);
        var delayMs = (int)Math.Max(1, baseMs + jitterMs);
        return TimeSpan.FromMilliseconds(delayMs);
    }

    private void RecordMetrics(long startTimestamp, int fetchedCount)
    {
        var elapsedMs = (long)_timeProvider.GetElapsedTime(startTimestamp).TotalMilliseconds;
        if (fetchedCount > 0)
        {
            _logger.BatchFetched(fetchedCount, elapsedMs);
            _metrics.BatchSize.Record(fetchedCount);
        }
    }

    private async Task UpdatePendingCountAsync(IOutboxRepository repo, CancellationToken cancellationToken)
    {
        var currentTicks = _timeProvider.GetTimestamp();
        if (_timeProvider.GetElapsedTime(Volatile.Read(ref _lastMetricTick)).TotalMilliseconds > _metricIntervalMs)
        {
            Volatile.Write(ref _lastMetricTick, currentTicks);
            var pendingCount = await repo.GetPendingCountAsync(cancellationToken);
            Interlocked.Exchange(ref _pendingCount, pendingCount);
        }
    }

    private async Task ProcessMessagesAsync(IReadOnlyList<OutboxMessage> messages, CancellationToken cancellationToken)
    {
        foreach (var message in messages)
        {
            await _channel.WriteAsync(message, cancellationToken);
        }
    }
}





