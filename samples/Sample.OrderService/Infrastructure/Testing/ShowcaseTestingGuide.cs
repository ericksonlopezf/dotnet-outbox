using System;
using System.Linq;
using System.Threading.Tasks;
using EricksonLopez.Outbox;
using EricksonLopez.Outbox.Persistence;
using EricksonLopez.Outbox.Testing;
using Sample.OrderService.Domain.Aggregates.OrderAggregate;

namespace Sample.OrderService.Infrastructure.Testing;

/// <summary>
/// Showcase Testing Guide
/// Demonstrates the full Unit Testing API of the Outbox library.
/// Uses <see cref="InMemoryOutboxStore"/> to avoid database dependencies in unit tests.
/// </summary>
public static class ShowcaseTestingGuide
{
    /// <summary>
    /// Demonstrates the complete flow: Setup, Execution, and Assertion.
    /// </summary>
    public static string DemoBasicTestingFlow()
    {
        // 1. SETUP: We instantiate the in-memory store.
        // This object replaces the real IOutbox in your unit tests.
        var inMemoryStore = new InMemoryOutboxStore();

        // 2. ACT: We execute the business logic that publishes messages.
        // In a real test, you pass inMemoryStore to your CommandHandler or Domain Service.
        SimulateBusinessOperation(inMemoryStore);

        // 3. ASSERTIONS: We use the fluent assertion API.
        
        // Option A: Direct assertions on the store using extension methods
        var publishedEvents = inMemoryStore.ShouldHavePublished<OrderCreatedEvent>();
        
        if (publishedEvents.Count != 1)
        {
            return "Test Failed: Expected 1 OrderCreatedEvent.";
        }

        var orderEvent = inMemoryStore.ShouldHavePublishedOnce<OrderCreatedEvent>();
        
        if (orderEvent.Total != 150m)
        {
            return "Test Failed: Total didn't match.";
        }

        // Option B: Fluent assertions using IOutboxTester
        // The store can be converted to a fluent tester
        var tester = new OutboxTesterImpl(inMemoryStore);

        try
        {
            // Assert that it was published exactly once
            tester.ShouldHavePublished<OrderCreatedEvent>()
                  .Once();

            // Assert that it was published with a specific condition
            tester.ShouldHavePublished<OrderCreatedEvent>()
                  .WithCondition(e => e.CustomerId == "CUST-TEST")
                  .Once();

            // Assert that another event was NOT published
            tester.ShouldHavePublished<OrderConfirmedEvent>()
                  .Never();

            // Assertions using extension shortcuts on the Tester
            tester.ShouldNotHavePublished<OrderConfirmedEvent>();
            tester.ShouldHavePublishedOnce<OrderCreatedEvent>(e => e.Total == 150m);
        }
        catch (System.Exception ex)
        {
            // The fluent assertions throw an exception if they fail,
            // which integrates perfectly with xUnit/NUnit/MSTest.
            return $"Test Failed during fluent assertion: {ex.Message}";
        }

        // 4. TEARDOWN (optional): clear the store between tests if it is shared
        inMemoryStore.Reset();

        return "Test Passed: All Testing API assertions ran successfully.";
    }

    /// <summary>
    /// Simulates a business method that publishes an event.
    /// </summary>
    private static void SimulateBusinessOperation(InMemoryOutboxStore store)
    {
        // InMemoryOutboxStore captures the message WITHOUT a real transaction.
        // The implementation ignores the IOutboxTransactionContext — you can pass
        // any instance. OutboxTransactionContext accepts any object
        // as "connection" and "transaction".
        var fakeTransaction = new EricksonLopez.Outbox.Persistence.OutboxTransactionContext(
            connection: new object(),
            transaction: new object());

        var @event = new OrderCreatedEvent(
            EventId: Guid.NewGuid(),
            CustomerId: "CUST-TEST",
            Total: 150m,
            OccurredOn: DateTimeOffset.UtcNow);

        // We store synchronously for the example.
        store.StoreAsync(@event, fakeTransaction).AsTask().Wait();
    }
}
