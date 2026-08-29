// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using EricksonLopez.Outbox;
using EricksonLopez.Outbox.Persistence;
using EricksonLopez.Outbox.Retry;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Sample.OrderService.Infrastructure.Customization;
using System.Threading.Tasks;

#pragma warning disable CA1861
namespace Sample.OrderService.Endpoints;

/// <summary>
/// Level 6 — Error Handling
/// Demonstrates DispatchResult, RetryPolicy (Fixed and Exponential), CircuitBreakerState,
/// and the system's behavior when facing transient and permanent failures.
/// </summary>
public static class Level6_ErrorHandlingEndpoints
{
    public static void MapLevel6ErrorHandling(this IEndpointRouteBuilder app)
    {
        // ─── Endpoint 6a: DispatchResult Information ───────────────────
        // Returns the table of valid DispatchResult states.
        // In real code, the publisher (ConsoleBrokerPublisher) returns DispatchResult.Ok().
        // Here we explain all 3 possible states.
        app.MapGet("/api/level6/dispatch-result-states", () =>
        {
            // DispatchResult is a readonly record struct with 3 valid states:
            var ok = DispatchResult.Ok();
            var retry = DispatchResult.FailAndRetry(new InvalidOperationException("Transient failure"));
            var retryNoIncrement = DispatchResult.FailAndRetry(new InvalidOperationException("Rate limited"), incrementRetryCount: false);
            var fatal = DispatchResult.FailFatal(new ArgumentException("Fatal failure"));
            var fatalFromString = DispatchResult.FailFatal("Schema validation failed");

            return Results.Ok(new
            {
                description = "The 5 factory methods of DispatchResult that an IBrokerPublisher must use:",
                states = new[]
                {
                    new { method = "DispatchResult.Ok()", success = ok.Success, shouldRetry = ok.ShouldRetry, incrementRetry = ok.IncrementRetryCount, use = "Successful publication. The dispatcher will remove the message from the DB." },
                    new { method = "DispatchResult.FailAndRetry(ex)", success = retry.Success, shouldRetry = retry.ShouldRetry, incrementRetry = retry.IncrementRetryCount, use = "Transient error (network, broker down). Retries with exponential backoff." },
                    new { method = "DispatchResult.FailAndRetry(ex, false)", success = retryNoIncrement.Success, shouldRetry = retryNoIncrement.ShouldRetry, incrementRetry = retryNoIncrement.IncrementRetryCount, use = "Rate limiting. Retries but DOES NOT increment the message's retry counter." },
                    new { method = "DispatchResult.FailFatal(ex)", success = fatal.Success, shouldRetry = fatal.ShouldRetry, incrementRetry = fatal.IncrementRetryCount, use = "Permanent error (schema mismatch, message too large). Immediate dead-letter." },
                    new { method = "DispatchResult.FailFatal(string)", success = fatalFromString.Success, shouldRetry = fatalFromString.ShouldRetry, incrementRetry = fatalFromString.IncrementRetryCount, use = "Permanent error without Exception. Useful when there is no original exception." },
                },
                rules = new[]
                {
                    "NEVER throw exceptions from PublishRawAsync — always catch them and map them to DispatchResult.",
                    "NEVER return default(DispatchResult) — it is an incoherent state that dead-letters the message.",
                    "Use ThrowIfInvalid() in tests to validate that the DispatchResult is coherent.",
                }
            });
        })
        .WithSummary("Level 6a - DispatchResult: states and factory methods")
        .WithTags("Level 6 — Error Handling");

        // ─── Endpoint 6b: Retry Policies ─────────────────────────────────────
        // RetryPolicy is the base class (abstract record) for retry policies
        // at the BROKER PUBLICATION level (not the outbox dispatcher).
        // Configures how many attempts the RetryDispatcherInterceptor makes on failures.
        //
        // Available built-in policies:
        //   • RetryPolicy.Default — exponential backoff, 4 attempts (1s, 2s, 4s, 8s, max 30s)
        //   • FixedDelayRetryPolicy — constant delay between all attempts
        //   • ExponentialBackoffRetryPolicy — delay doubles on each attempt
        //   • JitterRetryPolicy — exponential + random jitter to prevent thundering-herd problem
        app.MapGet("/api/level6/retry-policies", () =>
        {
            // RetryPolicy.Default: exponential backoff, 1s initial, max 30s, 5 attempts.
            var defaultPolicy = RetryPolicy.Default;

            // FixedDelayRetryPolicy: same delay between attempts.
            var fixedPolicy = new FixedDelayRetryPolicy(
                Delay: TimeSpan.FromSeconds(2),
                MaxAttempts: 3);

            // ExponentialBackoffRetryPolicy: delay grows exponentially.
            var exponentialPolicy = new ExponentialBackoffRetryPolicy(
                InitialDelay: TimeSpan.FromSeconds(1),
                MaxAttempts: 5,
                Factor: 2.0,       // delay * 2 on each attempt
                MaxDelay: TimeSpan.FromSeconds(30)); // maximum cap

            // JitterRetryPolicy: exponential backoff + random jitter (±25% by default).
            // Prevents the thundering-herd problem: when multiple dispatcher instances
            // all fail simultaneously, they will retry at slightly different times,
            // avoiding synchronized load spikes on the recovering broker.
            var jitterPolicy = new JitterRetryPolicy(
                InitialDelay: TimeSpan.FromSeconds(1),
                MaxAttempts: 5,
                Factor: 2.0,          // delay * 2 on each attempt (base)
                MaxDelay: TimeSpan.FromSeconds(30),
                JitterFactor: 0.25);  // ±25% random deviation of the base delay

            return Results.Ok(new
            {
                description = "The 4 available retry policies. Configured in UseBroker() when registering the IBrokerPublisher.",
                policies = new object[]
                {
                    new
                    {
                        type = "RetryPolicy.Default",
                        schedule = new[] {
                            $"Attempt 1: {defaultPolicy.GetNextDelay(1)?.TotalSeconds}s",
                            $"Attempt 2: {defaultPolicy.GetNextDelay(2)?.TotalSeconds}s",
                            $"Attempt 3: {defaultPolicy.GetNextDelay(3)?.TotalSeconds}s",
                            $"Attempt 4: {defaultPolicy.GetNextDelay(4)?.TotalSeconds}s",
                            $"Attempt 5: {defaultPolicy.GetNextDelay(5)?.TotalSeconds}s (null = stop)",
                        },
                        use = "General use. Sensible for most transient failures."
                    },
                    new
                    {
                        type = "FixedDelayRetryPolicy(2s, 3 attempts)",
                        schedule = new[] {
                            $"Attempt 1: {fixedPolicy.GetNextDelay(1)?.TotalSeconds}s",
                            $"Attempt 2: {fixedPolicy.GetNextDelay(2)?.TotalSeconds}s",
                            $"Attempt 3: {fixedPolicy.GetNextDelay(3)?.TotalSeconds}s (null = stop)",
                        },
                        use = "Brokers with predictable rate limiting (e.g., quota of 1 req/2s)."
                    },
                    new
                    {
                        type = "ExponentialBackoffRetryPolicy(1s, 5, x2, max30s)",
                        schedule = new[] {
                            $"Attempt 1: {exponentialPolicy.GetNextDelay(1)?.TotalSeconds}s",
                            $"Attempt 2: {exponentialPolicy.GetNextDelay(2)?.TotalSeconds}s",
                            $"Attempt 3: {exponentialPolicy.GetNextDelay(3)?.TotalSeconds}s",
                            $"Attempt 4: {exponentialPolicy.GetNextDelay(4)?.TotalSeconds}s",
                            $"Attempt 5: {exponentialPolicy.GetNextDelay(5)?.TotalSeconds}s (null = stop)",
                        },
                        use = "Network failures or saturated broker. Reduces pressure exponentially."
                    },
                    new
                    {
                        type = "JitterRetryPolicy(1s, 5, x2, max30s, jitter25%)",
                        description = "JitterFactor=0.25 means each delay = base ± (base * 25%) at random.",
                        approximateSchedule = new[]
                        {
                            "Attempt 1: ~1.0s ± 0.25s  (range: 0.75s–1.25s)",
                            "Attempt 2: ~2.0s ± 0.50s  (range: 1.50s–2.50s)",
                            "Attempt 3: ~4.0s ± 1.00s  (range: 3.00s–5.00s)",
                            "Attempt 4: ~8.0s ± 2.00s  (range: 6.00s–10.0s)",
                            "Attempt 5: null            (max attempts exhausted)",
                        },
                        use = "Recommended for multi-instance deployments. Prevents thundering-herd problem.",
                        parameters = new
                        {
                            InitialDelay = jitterPolicy.InitialDelay.TotalSeconds + "s",
                            MaxAttempts = jitterPolicy.MaxAttempts,
                            Factor = jitterPolicy.Factor,
                            MaxDelay = jitterPolicy.MaxDelay?.TotalSeconds + "s",
                            JitterFactor = jitterPolicy.JitterFactor
                        }
                    }
                },
                configurationExample = @"
// In AddOutbox(), pass the retryPolicy to UseBroker():
services.AddOutbox(options =>
{
    // Option A: ExponentialBackoff for single-instance deployments
    var retryPolicy = new ExponentialBackoffRetryPolicy(
        InitialDelay: TimeSpan.FromSeconds(1),
        MaxAttempts: 5,
        Factor: 2.0,
        MaxDelay: TimeSpan.FromSeconds(30));

    // Option B: JitterRetryPolicy for multi-instance deployments (recommended)
    var jitterPolicy = new JitterRetryPolicy(
        InitialDelay: TimeSpan.FromSeconds(1),
        MaxAttempts: 5,
        Factor: 2.0,
        MaxDelay: TimeSpan.FromSeconds(30),
        JitterFactor: 0.25);

    var circuitBreaker = new CircuitBreakerState(
        failureThreshold: 5,
        openDuration: TimeSpan.FromSeconds(30));

    options.UseBroker<ConsoleBrokerPublisher>(jitterPolicy, circuitBreaker);
});"
            });
        })
        .WithSummary("Level 6b - RetryPolicy: Default, FixedDelay, ExponentialBackoff, JitterRetryPolicy")
        .WithTags("Level 6 — Error Handling");

        // ─── Endpoint 6c: CircuitBreakerState ────────────────────────────────
        // CircuitBreakerState is a lightweight thread-safe state machine.
        // Prevents saturating a down broker by continuously sending messages.
        // Integrates with UseBroker() to automatically wrap the publisher.
        app.MapGet("/api/level6/circuit-breaker", () =>
        {
            var cb = new CircuitBreakerState(
                failureThreshold: 5,
                openDuration: TimeSpan.FromSeconds(30));

            // Initial state: Closed (normal operation)
            var initialState = cb.State;
            var allowsRequest = cb.AllowRequest();

            // Simulate 5 consecutive failures → circuit opens
            for (int i = 0; i < 5; i++) cb.RecordFailure();
            var stateAfterFailures = cb.State;
            var allowsAfterOpen = cb.AllowRequest();

            // Simulate recovery: success closes the circuit
            // (in HalfOpen → success → Closed)
            // Note: the circuit remains Open until OpenDuration elapses.
            // In this demo we don't wait 30s, we just show the API.

            return Results.Ok(new
            {
                description = "CircuitBreakerState — circuit breaker state for an IBrokerPublisher.",
                states = new object[]
                {
                    new { state = "Closed", meaning = "Normal. All publications pass." },
                    new { state = "Open", meaning = $"Too many failures ({cb.FailureThreshold}). Publications are immediately rejected without contacting the broker." },
                    new { state = "HalfOpen", meaning = $"{cb.OpenDuration.TotalSeconds}s passed. A single test call is allowed. If fails → Open. If ok → Closed." },
                },
                demo = new
                {
                    initialState = initialState.ToString(),
                    allowsRequestInitial = allowsRequest,
                    stateAfter5Failures = stateAfterFailures.ToString(),
                    allowsRequestOpen = allowsAfterOpen,
                    failureThreshold = cb.FailureThreshold,
                    openDurationSeconds = cb.OpenDuration.TotalSeconds,
                },
                methods = new[]
                {
                    "cb.AllowRequest() → bool — is the attempt allowed?",
                    "cb.RecordSuccess() → closes the circuit",
                    "cb.RecordFailure() → increments counter or opens if threshold reached",
                    "cb.State → CircuitState enum (Closed/Open/HalfOpen)",
                },
                configurationInProgram = @"
// In Program.cs with UseBroker():
var circuitBreaker = new CircuitBreakerState(
    failureThreshold: 5,
    openDuration: TimeSpan.FromSeconds(30));

services.AddOutbox(options =>
{
    options.UseBroker<ConsoleBrokerPublisher>(
        retryPolicy: RetryPolicy.Default,
        circuitBreaker: circuitBreaker);
});"
            });
        })
        .WithSummary("Level 6c - CircuitBreakerState: Closed/Open/HalfOpen")
        .WithTags("Level 6 — Error Handling");

        // ─── Endpoint 6d: OutboxMessageStatus — lifecycle states ────
        // OutboxMessageStatus is the enum reflecting the message state in the DB.
        // Knowing the states is fundamental to understanding the dispatcher's behavior.
        app.MapGet("/api/level6/message-status", () =>
        {
            var stateMachine = new[]
            {
                new { status = "Pending (0)", transition = "→ InFlight (1)", trigger = "FetchPendingAsync() claims the message with SKIP LOCKED" },
                new { status = "InFlight (1)", transition = "→ DELETE", trigger = "MarkAsDispatchedAsync() after successful publication (DeleteOnDispatch=true, default)" },
                new { status = "InFlight (1)", transition = "→ Dispatched (2) UPDATE", trigger = "MarkAsDispatchedAsync() when DeleteOnDispatch=false" },
                new { status = "InFlight (1)", transition = "→ Failed (3)", trigger = "MarkAsFailedAsync() after transient failure" },
                new { status = "Failed (3)", transition = "→ InFlight (1)", trigger = "FetchPendingAsync() retries it when deliver_at <= UtcNow (exponential backoff)" },
                new { status = "InFlight (1)", transition = "→ DeadLettered (4)", trigger = "MarkAsFailedAsync(isDeadLetter:true) when RetryCount >= MaxRetryCount" },
                new { status = "InFlight (1)", transition = "→ Pending (0)", trigger = "ReclaimStaleMessagesAsync() recovers blocked messages after dispatcher crash" },
            };

            return Results.Ok(new
            {
                description = "Lifecycle of an OutboxMessage. Each state corresponds to a value in the 'state' column of the outbox table.",
                note = "The value 2 (Reserved) is intentionally unassigned. Dispatched messages are DELETED by default (DeleteOnDispatch=true).",
                stateMachine
            });
        })
        .WithSummary("Level 6d - OutboxMessageStatus: message state machine")
        .WithTags("Level 6 — Error Handling");

        // ─── Endpoint 6e: Outbox Exceptions Validation ───────────────────────
        // Demonstrates the OutboxPayloadTooLargeException which is thrown during
        // StoreAsync if the payload exceeds MaxPayloadSizeInBytes.
        app.MapPost("/api/level6/payload-too-large", async (
            [FromServices] IOutbox outbox,
            [FromServices] Npgsql.NpgsqlDataSource dataSource,
            CancellationToken ct) =>
        {
            await using var conn = await dataSource.OpenConnectionAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);

            // Create a payload that is deliberately huge (e.g., 2MB string)
            // By default MaxPayloadSizeInBytes is 1MB.
            var hugeString = new string('A', 2 * 1024 * 1024);
            var @event = new Sample.OrderService.Domain.Aggregates.OrderAggregate.BatchTestEvent(1, hugeString);

            try
            {
                await outbox.Publish(@event)
                    .WithTransaction(tx.ToOutboxContext())
                    .StoreAsync(ct);
                    
                await tx.CommitAsync(ct);
                return Results.Ok("Message stored successfully (this shouldn't happen if max payload is 1MB).");
            }
            catch (EricksonLopez.Outbox.OutboxPayloadTooLargeException ex)
            {
                await tx.RollbackAsync(ct);
                return Results.Ok(new
                {
                    message = "Caught OutboxPayloadTooLargeException successfully.",
                    actualSize = ex.ActualSize,
                    maxAllowedSize = ex.MaxAllowedSize,
                    errorMessage = ex.Message
                });
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync(ct);
                return Results.Problem(ex.Message);
            }
        })
        .WithSummary("Level 6e - OutboxPayloadTooLargeException simulation")
        .WithTags("Level 6 — Error Handling");
    }
}


