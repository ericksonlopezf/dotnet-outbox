using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using EricksonLopez.Outbox;
using EricksonLopez.Outbox.Dispatcher;
using Microsoft.Extensions.DependencyInjection;

namespace Sample.OrderService.Endpoints;

/// <summary>
/// Level 7 — Scalability
/// Demonstrates configuration options of OutboxDispatcherOptions for horizontal
/// scaling, throughput, rate limiting, and the manual wake-up mechanism of the poller.
/// </summary>
public static class Level7_ScalabilityEndpoints
{
    public static void MapLevel7Scalability(this IEndpointRouteBuilder app)
    {
        // ─── Endpoint 7a: OutboxDispatcherOptions explained ─────────────────
        // All properties that control the dispatcher's behavior.
        app.MapGet("/api/level7/dispatcher-options", () =>
        {
            return Results.Ok(new
            {
                description = "OutboxDispatcherOptions — all configuration properties of the background service dispatcher.",
                properties = new[]
                {
                    new { property = "BatchSize", type = "int", defaultValue = "100", description = "Maximum messages read from DB per polling cycle. Increase for higher throughput; decrease for lower memory latency." },
                    new { property = "MaxDegreeOfParallelism", type = "int", defaultValue = "min(ProcessorCount, 8)", description = "Concurrent consumers of the internal channel. A value of 1 guarantees strict FIFO order. Increase for higher parallel throughput." },
                    new { property = "PollingInterval", type = "TimeSpan", defaultValue = "500ms", description = "Wait interval when the DB is empty. Only applies if UseAdaptivePolling=false." },
                    new { property = "UseAdaptivePolling", type = "bool", defaultValue = "true", description = "The poller dynamically adjusts the interval based on load: 0ms when there are messages, PollingInterval when empty." },
                    new { property = "ChannelCapacity", type = "int", defaultValue = "1000", description = "Maximum capacity of the System.Threading.Channels.Channel<T> between the poller and consumers. Provides natural backpressure." },
                    new { property = "MaxBatchesPerSecond", type = "int", defaultValue = "0 (no limit)", description = "Rate limiting on the dispatcher. Limits how many batches are processed per second during backlog drain. 0 = unlimited." },
                    new { property = "MaxRetryCount", type = "int", defaultValue = "10", description = "Number of retries before dead-lettering the message." },
                    new { property = "ReclaimTimeout", type = "TimeSpan", defaultValue = "5 min", description = "Maximum time a message can be InFlight before being reclaimed (crash recovery)." },
                    new { property = "ReclaimInterval", type = "TimeSpan", defaultValue = "1 min", description = "Frequency of the stale message reclamation job." },
                    new { property = "DbRetryMaxAttempts", type = "int", defaultValue = "3", description = "Retries of transient DB operations (MarkAsDispatched, MarkAsFailed)." },
                    new { property = "DbRetryBaseDelayMs", type = "int", defaultValue = "50ms", description = "Base delay for linear DB retry: attempt * DbRetryBaseDelayMs." },
                    new { property = "HasOnlySingletonMiddlewares", type = "bool", defaultValue = "false", description = "Optimization: if all middlewares are Singleton, the pipeline is cached and not rebuilt per batch." },
                },
                horizontalScalingNote = "The dispatcher uses SELECT FOR UPDATE SKIP LOCKED (PostgreSQL) / RowLock hints (SQL Server) " +
                    "for native distributed coordination. Multiple instances scale without extra configuration.",
                configExample = @"
services.AddOutboxDispatcher(options =>
{
    options.BatchSize = 500;                              // High throughput
    options.MaxDegreeOfParallelism = 4;                   // 4 parallel consumers
    options.UseAdaptivePolling = true;                    // Adaptive polling
    options.MaxBatchesPerSecond = 10;                     // Rate limit: max 10 batches/s
    options.MaxRetryCount = 5;                            // Dead-letter after 5 failures
    options.ReclaimTimeout = TimeSpan.FromMinutes(5);     // 5 min crash recovery
    options.HasOnlySingletonMiddlewares = true;            // Cache pipeline
});"
            });
        })
        .WithSummary("Level 7a - OutboxDispatcherOptions: complete property reference")
        .WithTags("Level 7 — Scalability");

        // ─── Endpoint 7b: OutboxRuntimeOptions explained ─────────────────────
        // OutboxRuntimeOptions controls the behavior of the producer and the store.
        app.MapGet("/api/level7/runtime-options", () =>
        {
            return Results.Ok(new
            {
                description = "OutboxRuntimeOptions — behavior configuration for the producer and the repository.",
                properties = new[]
                {
                    new { property = "SchemaName", defaultValue = "\"outbox\"", description = "DB schema where the outbox tables reside." },
                    new { property = "TableName", defaultValue = "\"messages\"", description = "Name of the messages table." },
                    new { property = "MaxPayloadSizeInBytes", defaultValue = "1 MB", description = "Maximum size of the JSON payload. Larger messages are rejected." },
                    new { property = "MaxHeaderSizeInBytes", defaultValue = "64 KB", description = "Maximum size of serialized headers." },
                    new { property = "ThrowOnUnregisteredType", defaultValue = "false", description = "If true, throws an exception when attempting to store a type not registered in the type resolver." },
                    new { property = "MaxMessageAge", defaultValue = "30 days", description = "Maximum age of a message in the DB. Also acts as an upper limit for deliver_at scheduling. If deliver_at > MaxMessageAge, the message is NEVER dispatched." },
                    new { property = "MaxBackoffSeconds", defaultValue = "3600s (1h)", description = "Maximum cap of exponential backoff for failed messages. Formula: POWER(2, retry_count) * 10, capped at MaxBackoffSeconds." },
                    new { property = "LargeTableThreshold", defaultValue = "50,000 rows", description = "Threshold where GetPendingCountAsync uses catalog estimates instead of exact COUNT(*) (PostgreSQL)." },
                    new { property = "DeleteOnDispatch", defaultValue = "true", description = "true = DELETE the message after dispatch (recommended). false = UPDATE to state=2 (for audit/replay, requires a cleanup job)." },
                    new { property = "MaxStoreRatePerSecond", defaultValue = "0 (no limit)", description = "Producer rate limiting. Prevents flooding the outbox table. 0 = no limit." },
                    new { property = "InstanceId", defaultValue = "Guid.NewGuid().ToString(\"N\")", description = "Unique identifier of this runtime instance. Auto-generated. Useful in multi-instance logs." },
                },
                warningAboutScheduling = "If you use WithDelay()/WithDeliverAt() with values > MaxMessageAge, the message will be trapped " +
                    "in the DB with status=Pending indefinitely. Set MaxMessageAge >= 1 day + maximum scheduling offset.",
                configExample = @"
services.AddOutbox(options =>
{
    options.ConfigureRuntimeOptions(runtime =>
    {
        runtime.SchemaName = ""myapp_outbox"";
        runtime.TableName = ""events"";
        runtime.MaxMessageAge = TimeSpan.FromDays(7);   // Messages up to 7 days
        runtime.DeleteOnDispatch = true;                // Recommended
        runtime.MaxStoreRatePerSecond = 1000;           // Producer rate limit
        runtime.MaxBackoffSeconds = 3600;               // Max 1h backoff
    });
});"
            });
        })
        .WithSummary("Level 7b - OutboxRuntimeOptions: complete property reference")
        .WithTags("Level 7 — Scalability");

        // ─── Endpoint 7c: IPollerWakeup — Manual wake-up of the poller ──────────
        // IPollerWakeup allows waking up the AdaptivePoller from external code.
        // Useful to force a polling cycle immediately after storing a critical
        // message, without waiting for the next interval.
        //
        // IPollerWakeup is registered as the implementation of AdaptivePoller.
        // It is injected directly from DI.
        app.MapPost("/api/level7/wake-up-poller", (
            [Microsoft.AspNetCore.Mvc.FromServices] IPollerWakeup pollerWakeup) =>
        {
            // WakeUp() wakes up the poller immediately.
            // If the poller is processing, the signal accumulates and applies at the end of the current cycle.
            pollerWakeup.WakeUp();

            return Results.Ok(new
            {
                message = "Poller manually woken up via IPollerWakeup.WakeUp().",
                description = "Useful after a critical StoreAsync() where you cannot wait for the PollingInterval.",
                note = "IPollerWakeup is available in DI only when AddOutboxDispatcher() has been called."
            });
        })
        .WithSummary("Level 7c - IPollerWakeup.WakeUp() — manual wake-up of the background dispatcher")
        .WithTags("Level 7 — Scalability");
    }
}
