// Copyright © Erickson Lopez. MIT License.
#pragma warning disable CA1861 // Prefer static readonly fields over constant array arguments
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.Outbox;
using EricksonLopez.Outbox.Hosting;
using EricksonLopez.Outbox.Persistence;
using EricksonLopez.Outbox.Testing;
using EricksonLopez.Result;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Sample.OrderService.Domain.Aggregates.OrderAggregate;
using Sample.OrderService.Infrastructure.Testing;

namespace Sample.OrderService.Endpoints;

/// <summary>
/// Level 9 — Extensions and Integrations
/// Demonstrates ManualOutboxDispatcher (serverless/cron), the full Testing API
/// (InMemoryOutboxStore, OutboxTesterImpl, fluent assertions).
/// </summary>
public static class Level9_ExtensionsEndpoints
{
    public static void MapLevel9Extensions(this IEndpointRouteBuilder app)
    {
        // ─── Endpoint 9a: ManualOutboxDispatcher ─────────────────────────────
        // ManualOutboxDispatcher allows triggering message dispatch
        // on demand — without needing the background service.
        //
        // Use cases:
        //   - Serverless functions (Azure Functions, AWS Lambda)
        //   - Periodic cron jobs
        //   - Administration endpoints that force a drain
        //   - Test environments needing synchronous dispatch
        app.MapPost("/api/level9/manual-dispatch", async (
            [FromServices] ManualOutboxDispatcher manualDispatcher,
            [FromServices] IOutboxRepository repository,
            CancellationToken ct) =>
        {
            // DispatchPendingAsync(repository, batchSize, ct):
            //   1. Calls FetchPendingAsync(batchSize) to get pending messages.
            //   2. Calls PublishRawAsync() for each message.
            //   3. Calls MarkAsDispatchedAsync() for the successful ones.
            //   4. Returns the number of messages dispatched.
            var dispatched = await manualDispatcher.DispatchPendingAsync(
                repository,
                batchSize: 50,
                cancellationToken: ct);

            return Results.Ok(new
            {
                message = $"ManualOutboxDispatcher: {dispatched} messages dispatched manually.",
                description = "Useful in serverless environments where AddOutboxDispatcher() is not used.",
                registrationNote = "ManualOutboxDispatcher requires manual registration in DI: services.AddScoped<ManualOutboxDispatcher>().",
            });
        })
        .WithSummary("Level 9a - ManualOutboxDispatcher.DispatchPendingAsync(): dispatch on demand")
        .WithTags("Level 9 — Extensions");

        // ─── Endpoint 9b: Complete Testing API ───────────────────────────────
        // The namespace EricksonLopez.Outbox.Testing provides a full API for
        // unit tests without a database:
        //   - InMemoryOutboxStore: implements IOutbox, captures messages in RAM.
        //   - OutboxTesterImpl: fluent assertions.
        //   - TestingOutboxExtensions: direct assertion methods on the store.
        app.MapGet("/api/level9/testing-api", () =>
        {
            // DemoBasicTestingFlow executes the complete testing cycle:
            // setup → act → assert with all available APIs.
            var result = ShowcaseTestingGuide.DemoBasicTestingFlow();

            return Results.Ok(new
            {
                message = result,
                description = "Complete testing API of the Outbox — without database, without mocks.",
                components = new string[]
                {
                    "InMemoryOutboxStore — IOutbox in RAM. Captures messages. Methods: StoreAsync<T>, StoreAsync(ReadOnlyMemory<T>), StoreAsync(msg,tx,metadata,deliverAt), Publish<T>, GetPublishedMessages<T>, Reset.",
                    "OutboxTesterImpl — wraps InMemoryOutboxStore. Method: ShouldHavePublished<T>() → IOutboxAssertion<T>.",
                    "IOutboxAssertion<T> — chainable assertions: WithCondition, Once, Times(n), AtLeastOnce, Never.",
                    "TestingOutboxExtensions (InMemoryOutboxStore) — ShouldHavePublished<T>(), ShouldHavePublished<T>(predicate), ShouldHavePublishedOnce<T>(), ShouldHavePublishedOnce<T>(predicate), ShouldHavePublishedTimes<T>(n), ShouldNotHavePublished<T>(), ShouldNotHavePublished<T>(predicate), TotalPublishedCount.",
                    "TestingOutboxExtensions (IOutboxTester) — ShouldHavePublishedOnce<T>(), ShouldHavePublishedOnce<T>(predicate), ShouldHavePublished<T>(predicate), ShouldHavePublishedTimes<T>(n), ShouldNotHavePublished<T>.",
                    "FakeBrokerPublisher — IBrokerPublisher without a real broker. For integration tests of the dispatcher.",
                    "FakeOutboxDispatcher — alternative IOutbox that captures messages as the dispatcher would see them.",
                    "FakeOutboxRepository — IOutboxRepository in RAM. For tests of the background service dispatcher.",
                    "FakeIdempotencyRepository — IIdempotencyRepository in RAM. For tests of IInboxIdempotencyChecker.",
                }
            });
        })
        .WithSummary("Level 9b - Testing API: InMemoryOutboxStore, OutboxTesterImpl, assertions")
        .WithTags("Level 9 — Extensions");
    }
}



