<!-- Copyright © Erickson Lopez. MIT License. -->

# Level 12: Testing Guide

This level is the complete reference for testing code that uses `EricksonLopez.Outbox`. All testing utilities ship in the **core** `EricksonLopez.Outbox` package under the `EricksonLopez.Outbox.Testing` namespace — no additional package required.

## 1. Testing Toolkit Overview

| Type | Implements | Purpose |
|---|---|---|
| `InMemoryOutboxStore` | `IOutbox` | Captures stored messages in-memory for unit test assertions. |
| `OutboxTesterImpl` | `IOutboxTester` | Wraps `InMemoryOutboxStore` and adds fluent assertion builder. |
| `FakeBrokerPublisher` | `IBrokerPublisher` | Records publish calls; returns configurable `DispatchResult`. |
| `FakeOutboxRepository` | `IOutboxRepository` | In-memory store that simulates fetch/mark/count operations. |
| `FakeDeadLetterRepository` | `IDeadLetterRepository` | In-memory DLQ for asserting dead-lettered messages. |
| `FakeIdempotencyRepository` | `IIdempotencyRepository` | In-memory idempotency store; returns configurable `bool`. |
| `FakeInboxIdempotencyChecker` | `IInboxIdempotencyChecker` | Configurable: always process, always skip, or per-ID control. |
| `FakeOutboxDispatcher` | — | Manually dispatches messages through a pipeline for dispatcher tests. |

---

## 2. `InMemoryOutboxStore` — Unit Testing Producers

`InMemoryOutboxStore` is the primary test double for `IOutbox`. Register it in DI (or create directly) and swap it for the real `DefaultOutbox`:

```csharp
using EricksonLopez.Outbox.Testing;
using Microsoft.Extensions.DependencyInjection;

// Option A: Direct instantiation
var store = new InMemoryOutboxStore();
var sut = new OrderService(_dbContext, store);

// Option B: DI overrides
services.AddSingleton<IOutbox, InMemoryOutboxStore>();
// or:
var store = new InMemoryOutboxStore();
services.AddSingleton<IOutbox>(store);
```

### Asserting with Direct Extensions

```csharp
using EricksonLopez.Outbox.Testing;
using Xunit;

[Fact]
public async Task PlaceOrder_StoresOneOrderCreatedEvent()
{
    // Arrange
    var store = new InMemoryOutboxStore();
    var service = new OrderService(store);

    // Act
    await service.PlaceOrderAsync(new CreateOrderCommand("cust-001", 250m), default);

    // Assert: at least one of type:
    store.ShouldHavePublished<OrderCreatedEvent>();

    // Assert: exactly one of type — returns the message:
    OrderCreatedEvent evt = store.ShouldHavePublishedOnce<OrderCreatedEvent>();
    Assert.Equal("cust-001", evt.CustomerId);
    Assert.Equal(250m, evt.Total);

    // Assert: exactly one matching predicate:
    store.ShouldHavePublishedOnce<OrderCreatedEvent>(e => e.CustomerId == "cust-001");

    // Assert: at least one matching predicate (returns all matches):
    IReadOnlyList<OrderCreatedEvent> matches =
        store.ShouldHavePublished<OrderCreatedEvent>(e => e.Total > 100);

    // Assert: exact count:
    store.ShouldHavePublishedTimes<OrderCreatedEvent>(count: 1);

    // Assert: not published:
    store.ShouldNotHavePublished<OrderCancelledEvent>();

    // Assert: no matching predicate:
    store.ShouldNotHavePublished<OrderCreatedEvent>(e => e.Total < 0);

    // Assert: total count across all types:
    Assert.Equal(1, store.TotalPublishedCount());
}
```

### Resetting Between Tests

```csharp
// In a beforeEach / fixture cleanup:
store.Clear(); // Removes all captured messages
```

### `InMemoryOutboxStore` Direct API

| Method | Returns | Description |
|---|---|---|
| `GetPublishedMessages<TMessage>()` | `IReadOnlyList<TMessage>` | All captured messages of type `TMessage`. |
| `Clear()` | `void` | Clears all captured messages. |
| `StoreAsync<TMessage>(message, tx, ct)` | `ValueTask` | Captures the message (ignores transaction context). |
| `StoreAsync<TMessage>(ReadOnlyMemory<TMessage>, tx, ct)` | `ValueTask` | Captures all messages in the batch. |
| `Publish<TMessage>(message)` | `OutboxMessageBuilder<TMessage>` | Returns a builder (finalizes on `StoreAsync`). |

---

## 3. `OutboxTesterImpl` — Fluent Assertion Builder

When you prefer a fluent, expressive assertion style, use `OutboxTesterImpl` which wraps `InMemoryOutboxStore`:

```csharp
using EricksonLopez.Outbox.Testing;
using Xunit;

[Fact]
public async Task ImportProducts_StoresProductImportedForEach()
{
    // Arrange
    var store = new InMemoryOutboxStore();
    IOutboxTester tester = new OutboxTesterImpl(store); // implements IOutboxTester
    var service = new ProductImportService(store);

    // Act
    await service.ImportAsync(new[] { product1, product2, product3 }, default);

    // Assert — fluent chain
    tester.ShouldHavePublished<ProductImportedEvent>()
          .Times(3);

    tester.ShouldHavePublished<ProductImportedEvent>()
          .WithCondition(e => e.ProductId == product1.Id)
          .Once();

    // Shorthand extensions on IOutboxTester:
    tester.ShouldHavePublishedOnce<OrderCreatedEvent>();    // Asserts exactly once
    tester.ShouldNotHavePublished<OrderCancelledEvent>();   // Asserts never published
    tester.ShouldHavePublishedTimes<ProductImportedEvent>(3);
}
```

### `IOutboxAssertion<T>` API

All methods are obtained by chaining after `tester.ShouldHavePublished<T>()`:

| Method | Description |
|---|---|
| `.WithCondition(Func<T, bool> predicate)` | Narrows assertion to messages matching the predicate. Chainable. |
| `.Once()` | Asserts exactly 1 match. Throws `InvalidOperationException` otherwise. |
| `.Times(int count)` | Asserts exactly `count` matches. |
| `.AtLeastOnce()` | Asserts at least 1 match. |
| `.Never()` | Asserts 0 matches. |

### `TestingOutboxExtensions` — Shorthand on `IOutboxTester`

| Extension Method | Equivalent |
|---|---|
| `tester.ShouldHavePublishedOnce<T>()` | `tester.ShouldHavePublished<T>().Once()` |
| `tester.ShouldHavePublishedOnce<T>(predicate)` | `tester.ShouldHavePublished<T>().WithCondition(p).Once()` |
| `tester.ShouldHavePublished<T>(predicate)` | `tester.ShouldHavePublished<T>().WithCondition(p).AtLeastOnce()` |
| `tester.ShouldNotHavePublished<T>()` | `tester.ShouldHavePublished<T>().Never()` |
| `tester.ShouldHavePublishedTimes<T>(n)` | `tester.ShouldHavePublished<T>().Times(n)` |

---

## 4. `FakeBrokerPublisher` — Testing Dispatch Logic

Use `FakeBrokerPublisher` to verify what messages your custom dispatcher or middleware publishes, and to simulate broker failures:

```csharp
using EricksonLopez.Outbox.Testing;

[Fact]
public async Task Dispatcher_PublishesMessageToFakeBroker()
{
    // Arrange
    var fakeBroker = new FakeBrokerPublisher(); // Always returns DispatchResult.Ok() by default
    var repository = new FakeOutboxRepository();
    repository.Enqueue(new OutboxMessage { /* ... */ }); // Pre-populate with a pending message

    // Act — manually dispatch
    var dispatcher = new ManualOutboxDispatcher(serviceProvider, fakeBroker, typeResolver);
    int dispatched = await dispatcher.DispatchPendingAsync(repository, batchSize: 10, default);

    // Assert
    Assert.Equal(1, dispatched);
    Assert.Single(fakeBroker.PublishedMessages);
}

[Fact]
public async Task Dispatcher_HandlesPublishFailure()
{
    // Arrange — configure the fake to always return a transient failure
    var fakeBroker = new FakeBrokerPublisher(
        resultFactory: (message, metadata, ctx) =>
            ValueTask.FromResult(
                DispatchResult.FailAndRetry(         // ✅ Correct: not DispatchResult.Failure()
                    new BrokerUnavailableException("Broker is down"))));

    // ... rest of test
}
```

---

## 5. `FakeInboxIdempotencyChecker` — Testing Consumers

```csharp
using EricksonLopez.Outbox.Testing;

[Fact]
public async Task Consumer_SkipsDuplicate()
{
    // Arrange — configure to simulate a duplicate (already processed)
    var fakeChecker = new FakeInboxIdempotencyChecker(shouldProcess: false);
    var consumer = new BillingConsumer(fakeChecker, _db);

    // Act
    await consumer.HandleAsync(evt, messageId: "msg-001", transaction: null!, ct: default);

    // Assert — business logic should NOT have been called (invoice not created)
    Assert.Empty(_db.Invoices);
}

[Fact]
public async Task Consumer_ProcessesFirstMessage()
{
    // Arrange — configure to simulate first-time processing
    var fakeChecker = new FakeInboxIdempotencyChecker(shouldProcess: true);
    var consumer = new BillingConsumer(fakeChecker, _db);

    // Act
    await consumer.HandleAsync(evt, messageId: "msg-002", transaction: null!, ct: default);

    // Assert — invoice was created
    Assert.Single(_db.Invoices);
}
```

---

## 6. `FakeOutboxRepository` — Testing Dispatcher Logic

`FakeOutboxRepository` lets you simulate the database store when testing dispatcher or background service logic without a real database:

```csharp
using EricksonLopez.Outbox.Testing;

[Fact]
public async Task ManualDispatcher_ProcessesPendingMessages()
{
    // Arrange
    var repo = new FakeOutboxRepository();
    var broker = new FakeBrokerPublisher();

    // Pre-populate with messages in Pending state
    repo.Enqueue(TestOutboxMessages.Create("order-created-v1", payload: ordPayload));
    repo.Enqueue(TestOutboxMessages.Create("user-registered-v1", payload: userPayload));

    var dispatcher = new ManualOutboxDispatcher(serviceProvider, broker, typeResolver);

    // Act
    int count = await dispatcher.DispatchPendingAsync(repo, batchSize: 10, default);

    // Assert
    Assert.Equal(2, count);
    Assert.Empty(repo.GetPendingMessages()); // All dispatched (deleted from outbox)
    Assert.Equal(2, broker.PublishedMessages.Count);
}
```

---

## 7. Full Unit Test Example (xUnit + InMemoryOutboxStore)

```csharp
using EricksonLopez.Outbox.Testing;
using Xunit;

public class OrderServiceTests
{
    private readonly InMemoryOutboxStore _outboxStore = new();
    private readonly FakeOrderRepository _orderRepository = new();

    private OrderService CreateSut()
        => new(_orderRepository, _outboxStore);

    [Fact]
    public async Task PlaceOrder_StoresOrderCreatedEvent_WithCorrectData()
    {
        // Arrange
        var cmd = new CreateOrderCommand(CustomerId: "cust-001", Total: 150.00m);
        var sut = CreateSut();

        // Act
        await sut.PlaceOrderAsync(cmd, CancellationToken.None);

        // Assert — the event was stored exactly once with correct payload
        var evt = _outboxStore.ShouldHavePublishedOnce<OrderCreatedEvent>();
        Assert.Equal("cust-001", evt.CustomerId);
        Assert.Equal(150.00m, evt.Total);
    }

    [Fact]
    public async Task PlaceOrder_DoesNotStoreCancellationEvent()
    {
        // Arrange
        var cmd = new CreateOrderCommand("cust-001", 50m);
        var sut = CreateSut();

        // Act
        await sut.PlaceOrderAsync(cmd, CancellationToken.None);

        // Assert
        _outboxStore.ShouldNotHavePublished<OrderCancelledEvent>();
    }

    [Theory]
    [InlineData("cust-A", 100)]
    [InlineData("cust-B", 250)]
    [InlineData("cust-C", 9.99)]
    public async Task PlaceOrder_StoresCorrectCustomerId(string customerId, decimal total)
    {
        // Arrange
        _outboxStore.Clear(); // Reset between theory runs
        var sut = CreateSut();

        // Act
        await sut.PlaceOrderAsync(new CreateOrderCommand(customerId, total), default);

        // Assert
        _outboxStore.ShouldHavePublished<OrderCreatedEvent>(e => e.CustomerId == customerId);
    }

    [Fact]
    public async Task PlaceMultipleOrders_StoresOneEventPerOrder()
    {
        // Arrange
        var sut = CreateSut();
        var commands = Enumerable.Range(1, 5)
            .Select(i => new CreateOrderCommand($"cust-{i:000}", i * 100m));

        // Act
        foreach (var cmd in commands)
            await sut.PlaceOrderAsync(cmd, default);

        // Assert
        _outboxStore.ShouldHavePublishedTimes<OrderCreatedEvent>(count: 5);
        Assert.Equal(5, _outboxStore.TotalPublishedCount());
    }
}
```

---

## 8. Integration Test Pattern (Testcontainers)

For end-to-end integration tests against a real PostgreSQL database:

```csharp
using EricksonLopez.Outbox.Testing;
using EricksonLopez.Outbox.Storage.PostgreSql;
using Testcontainers.PostgreSql;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Xunit;

public class OutboxIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    private IHost? _host;
    private FakeBrokerPublisher _broker = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        _broker = new FakeBrokerPublisher();

        _host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                var ds = NpgsqlDataSource.Create(_postgres.GetConnectionString());
                services.AddSingleton(ds);
                services.AddScoped<IOutboxRepository, PostgreSqlOutboxRepository>();
                services.AddScoped<IDeadLetterRepository, PostgreSqlDeadLetterRepository>();
                services.AddSingleton<IBrokerPublisher>(_broker);
                services.AddOutbox(opt =>
                {
                    opt.UseSerializer(new NativeAotJsonSerializer(TestJsonContext.Default));
                    opt.UseGeneratedTypes();
                });
                services.AddOutboxDispatcher(opt => opt.BatchSize = 10);
            })
            .Build();

        // Apply schema migrations before starting
        await using var conn = await ds.OpenConnectionAsync();
        await ApplySchemaAsync(conn);

        await _host.StartAsync();
    }

    [Fact]
    public async Task StoreAsync_MessageIsDispatchedToBroker()
    {
        // Arrange
        var outbox = _host!.Services.GetRequiredService<IOutbox>();
        await using var tx = await GetTransactionAsync();
        var txCtx = new DbTransactionContext(tx);

        // Act — store the event
        await outbox.StoreAsync(
            new OrderCreatedEvent(Guid.NewGuid(), "cust-001", 99.99m),
            txCtx,
            default);
        await tx.CommitAsync();

        // Wait for the dispatcher to pick it up (AdaptivePoller + 500ms max)
        await Task.Delay(1500);

        // Assert — the fake broker received the message
        Assert.Single(_broker.PublishedMessages);
        Assert.Equal("order-created-v1", _broker.PublishedMessages[0].MessageType);
    }

    public async Task DisposeAsync()
    {
        if (_host is not null)
            await _host.StopAsync();
        await _postgres.DisposeAsync();
    }
}
```

---

**You have reached the end of the `EricksonLopez.Outbox` Showcase.**

| Level | Topic |
|---|---|
| [Level 0](level-00-introduction.md) | Introduction and Pattern Overview |
| [Level 1](level-01-getting-started.md) | Getting Started — First Event |
| [Level 2](level-02-configuration.md) | Full Configuration Reference |
| [Level 3](level-03-real-use-cases.md) | Real-World Use Cases |
| [Level 4](level-04-domain-events.md) | Domain Events and Integration Events |
| [Level 5](level-05-processing.md) | Processing and Dispatching |
| [Level 6](level-06-error-handling.md) | Error Handling, Retries, DLQ |
| [Level 7](level-07-scalability.md) | Scalability and Deployment |
| [Level 8](level-08-customization.md) | Customization and Extensibility |
| [Level 9](level-09-extensions.md) | Framework Integrations |
| [Level 10](level-10-enterprise-architecture.md) | Enterprise Architecture |
| [Level 11](level-11-administration.md) | Administration and Monitoring |
| **Level 12** | **Testing Guide** ← You are here |
| [Level 13](level-13-diagnostics.md) | Diagnostics and Observability |
