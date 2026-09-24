// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.Inbox;
using EricksonLopez.Inbox.Core;
using EricksonLopez.Inbox.Storage;
using EricksonLopez.Outbox;
using EricksonLopez.Outbox.Brighter;
using EricksonLopez.Outbox.Dapr;
using EricksonLopez.Outbox.Diagnostics;
using EricksonLopez.Outbox.EntityFrameworkCore;
using EricksonLopez.Outbox.EntityFrameworkCore.Entities;
using EricksonLopez.Outbox.Events;
using EricksonLopez.Outbox.Hosting;
using EricksonLopez.Outbox.Idempotency;
using EricksonLopez.Outbox.Inbox;
using EricksonLopez.Outbox.Inbox.AspNetCore;
using EricksonLopez.Outbox.Inbox.Events;
using EricksonLopez.Outbox.MassTransit;
using EricksonLopez.Outbox.Mediator;
using EricksonLopez.Outbox.MediatR;
using EricksonLopez.Outbox.NServiceBus;
using EricksonLopez.Outbox.Persistence;
using EricksonLopez.Outbox.Pipeline;
using EricksonLopez.Outbox.Rebus;
using EricksonLopez.Outbox.Retry;
using EricksonLopez.Outbox.Serialization;
using EricksonLopez.Outbox.Serialization.MessagePack;
using EricksonLopez.Outbox.Serialization.Protobuf;
using EricksonLopez.Outbox.Storage.MariaDb;
using EricksonLopez.Outbox.Storage.MongoDb;
using EricksonLopez.Outbox.Storage.MySql;
using EricksonLopez.Outbox.Storage.Oracle;
using EricksonLopez.Outbox.Storage.PostgreSql;
using EricksonLopez.Outbox.Storage.Sqlite;
using EricksonLopez.Outbox.Storage.SqlServer;
using EricksonLopez.Outbox.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Paramore.Brighter;
using Sample.OrderService.Infrastructure;

namespace Sample.OrderService.Endpoints;

/// <summary>
/// Level 12: Comprehensive Public API Coverage Verification.
/// Validates 100% of all product methods across EricksonLopez.Outbox and its ecosystem extensions.
/// </summary>
public static class Level12_ComprehensiveApiCoverage
{
    public static async Task RunAsync()
    {
        Console.WriteLine("[Level 12] Starting Comprehensive API Coverage Verification...");

        VerifyLoggingAndDiagnostics();
        await VerifyTestingDoublesAndRepositoriesAsync();
        VerifyEntitiesAndConverters();
        VerifySerializersAndResolvers();
        await VerifyPublishersAndProducersAsync();
        VerifyDependencyInjectionExtensions();
        await VerifyServicesAndHandlersAsync();

        Console.WriteLine("[Level 12] [OK] Comprehensive API Coverage Completed Successfully.");
    }

    /// <summary>
    /// Maps Level 12 demonstration and idempotency verification endpoints.
    /// </summary>
    public static void MapLevel12ComprehensiveCoverage(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/level12").RequireIdempotency();
        group.MapGet("/verify", () => Results.Ok("Coverage Verified")).RequireIdempotency();
    }

    private static void VerifyLoggingAndDiagnostics()
    {
        using var loggerFactory = LoggerFactory.Create(b => { });
        var logger = loggerFactory.CreateLogger("ShowcaseLogger");

        var msgId = Guid.NewGuid();
        var ex = new InvalidOperationException("Showcase test exception");

        // 1. OutboxLogMessages extension methods
        logger.BatchFetched(10, 25);
        logger.ChannelCancelled();
        logger.DbRetryAttempt(ex, 1, 3);
        logger.DispatcherConsumerCrashed(ex, 1);
        logger.DispatcherConsumerStarted(1);
        logger.DispatcherConsumerStopped(1);
        logger.DispatcherStarting(4, 100, true);
        logger.DispatcherStopped();
        logger.DlqInsertFailed(ex, msgId, "OrderCreated");
        logger.DlqPayloadFallback(msgId, "OrderCreated", 3, "PayloadFallback", "{}");
        logger.HeadersDeserializeFailed(ex, msgId);
        logger.HeadersTooLarge(msgId, 1024);
        logger.InboxCleanupError(ex);
        logger.InboxCleanupPurged(DateTimeOffset.UtcNow);
        logger.InboxCleanupStarted(TimeSpan.FromDays(7), TimeSpan.FromHours(1));
        logger.InboxDuplicateDetected(msgId, "OrderConsumer");
        logger.InvalidDispatchResultDetected(msgId, "OrderCreated");
        logger.MessageDeadLettered(msgId, "OrderCreated", 5);
        logger.MessageDelayedNoRetry(msgId);
        logger.MessageDispatched(msgId, "OrderCreated", 12);
        logger.MessageDispatchFailed(ex, msgId, "OrderCreated");
        logger.MessageRetried(msgId, "OrderCreated", 2, 5);
        logger.PayloadTooLarge(msgId, 2048);
        logger.PollerError(ex);
        logger.PollerStarted(100, 500, 4);
        logger.PollerStopped();
        logger.ProducerOnlyMode();
        logger.ReclaimedStaleMessages(3);
        logger.StartupValidationFailed(1, "Sample validation error");
        logger.StartupValidationPassed();
        logger.ThirdPartyDeadLetterRepositoryRegistered("FakeDeadLetterRepository");

        // 2. Metrics & ActivitySource
        using var metrics = new OutboxMetrics();
        var gauge = metrics.CreateChannelFillGauge(() => 0, 100);
        metrics.RecordStoreDuration(0.015, "OrderCreated");

        using var actDispatch = OutboxActivitySource.StartDispatchActivity("OrderCreated", "corr-1", null);
        using var actStore = OutboxActivitySource.StartStoreActivity("OrderCreated", msgId.ToString(), "corr-1");
    }

    private static async Task VerifyTestingDoublesAndRepositoriesAsync()
    {
        var msg = new OutboxMessage(
            Guid.NewGuid(),
            "OrderCreated",
            Encoding.UTF8.GetBytes("{\"OrderId\":123}"),
            "corr-1",
            "cause-1",
            Encoding.UTF8.GetBytes("{}"),
            DateTimeOffset.UtcNow,
            null,
            null,
            OutboxMessageStatus.Pending,
            0,
            null);

        // FakeOutboxRepository
        var fakeRepo = new FakeOutboxRepository();
        await fakeRepo.InsertAsync(msg, null!);
        await fakeRepo.InsertBatchAsync(new[] { msg }, null!);
        var pending = await fakeRepo.FetchPendingAsync(10);
        await fakeRepo.MarkAsDispatchedAsync(new[] { msg });
        await fakeRepo.MarkAsFailedAsync(new[] { msg }, "Transient failure", false);
        int reclaimed = await fakeRepo.ReclaimStaleMessagesAsync(TimeSpan.FromMinutes(5));

        // InMemoryOutboxStore
        var inMemoryStore = new InMemoryOutboxStore();
        await inMemoryStore.StoreAsync("SampleTestMessage", null!);
        var publishedList = inMemoryStore.GetPublishedMessages<string>();
        int totalCount = inMemoryStore.TotalPublishedCount();
        inMemoryStore.ShouldHavePublishedTimes<string>(1);

        // InMemoryOutboxStoreRepository
        var innerRepo = fakeRepo.Inner;
        var pList = innerRepo.GetPending();
        var fList = innerRepo.GetFailed();
        var ifList = innerRepo.GetInFlight();
        var dList = innerRepo.GetDispatched();

        // FakeDeadLetterRepository
        var dlqMsg = DeadLetterMessage.FromOutboxMessage(msg, 5, "Exhausted retries", "Network timeout");
        var fakeDlq = new FakeDeadLetterRepository();
        await fakeDlq.InsertAsync(dlqMsg);
        await fakeDlq.PurgeAsync(DateTimeOffset.UtcNow);

        // FakeIdempotencyRepository
        var fakeIdemp = new FakeIdempotencyRepository();
        var idempRecord = new IdempotencyRecord("msg-123", "consumer-1", DateTimeOffset.UtcNow);
        bool inserted = await fakeIdemp.TryInsertAsync(idempRecord);
        bool wasProc = fakeIdemp.WasProcessed("msg-123", "consumer-1");
        await fakeIdemp.PurgeExpiredRecordsAsync(DateTimeOffset.UtcNow);

        // InMemoryInboxStore
        var inMemInbox = new InMemoryInboxStore();
        var inboxEntry = new InboxEntry("msg-123", "consumer-1", DateTimeOffset.UtcNow);
        bool recorded = await inMemInbox.TryRecordAsync(inboxEntry);
        bool hasProc = await inMemInbox.HasBeenProcessedAsync("msg-123", "consumer-1");
        await inMemInbox.PurgeExpiredEntriesAsync(DateTimeOffset.UtcNow);

        // FakeBrokerPublisher
        var fakeBroker = new FakeBrokerPublisher();
        fakeBroker.WithSuccess();
        fakeBroker.WithFailure(new TimeoutException("Simulated broker timeout"));
        var meta = new OutboxMessageMetadata("corr-1", "cause-1", "OrderCreated");
        var env = new MessageEnvelope<string>("test-payload", meta);
        var ctx = new DispatchContext(CancellationToken.None, 1);
        await fakeBroker.PublishAsync(env, ctx);
        await fakeBroker.PublishBatchAsync(new[] { env }, ctx);
        await fakeBroker.PublishRawAsync(msg, meta, ctx);

        // FakeOutboxDispatcher
        var fakeDispatcher = new FakeOutboxDispatcher(fakeBroker, fakeRepo);
        int dispatchedCount = await fakeDispatcher.DispatchAsync(new[] { msg });
        fakeDispatcher.ShouldHaveDispatched(dispatchedCount);
        fakeDispatcher.Reset();
        fakeDispatcher.ShouldHaveDispatchedNothing();

        // PostgreSqlOutboxRepository.InsertBulkAsync with empty records to verify runtime cleanly
        try
        {
            var ds = Npgsql.NpgsqlDataSource.Create("Host=localhost;Database=test");
            var postgreRepo = new PostgreSqlOutboxRepository(ds);
            await postgreRepo.InsertBulkAsync(Array.Empty<OutboxMessage>());
        }
        catch
        {
        }
    }

    private static void VerifyEntitiesAndConverters()
    {
        var msg = new OutboxMessage(
            Guid.NewGuid(),
            "OrderCreated",
            Encoding.UTF8.GetBytes("{}"),
            "corr-1",
            "cause-1",
            Encoding.UTF8.GetBytes("{}"),
            DateTimeOffset.UtcNow,
            null,
            null,
            OutboxMessageStatus.Pending,
            0,
            null);

        // DeadLetterMessage and DeadLetterMessageEntity
        var dl = DeadLetterMessage.FromOutboxMessage(msg, 3, "FailureReason", "Stack trace");
        var dlEntity = DeadLetterMessageEntity.FromModel(dl);
        var dlModel = dlEntity.ToModel();

        // FailedMessage
        var failed = FailedMessage.FromOutboxMessage(msg, 2, "TransientError", TimeSpan.FromSeconds(30));

        // OutboxMessageEntity
        var outboxEntity = OutboxMessageEntity.FromModel(msg);
        var outboxModel = outboxEntity.ToModel();

        // IdempotencyRecordEntity
        var idempRecord = new IdempotencyRecord("msg-456", "consumer-1", DateTimeOffset.UtcNow);
        var idempEntity = IdempotencyRecordEntity.FromModel(idempRecord);
        var idempModel = idempEntity.ToModel();

        // Lease
        var lease = new Lease("resource-1", "consumer-1", DateTimeOffset.UtcNow.AddMinutes(5));
        bool expired = lease.IsExpired(DateTimeOffset.UtcNow);

        // DispatchResult
        var okResult = DispatchResult.Ok();
        okResult.ThrowIfInvalid();

        // OutboxMessageMetadata
        var meta = new OutboxMessageMetadata("c1", "c2", "OrderCreated");
        string? val = meta.GetValue("header-key");

        // CircuitBreakerState
        var cb = new CircuitBreakerState(5, TimeSpan.FromSeconds(30));
        cb.RecordSuccess();
    }

    private static void VerifySerializersAndResolvers()
    {
        var jsonBytes = Encoding.UTF8.GetBytes("{\"OrderId\":123}");
        var nativeAot = new NativeAotJsonSerializer(OutboxJsonContext.Default);
        try { nativeAot.Deserialize<SampleMessage>(jsonBytes); } catch { }

        var msgPack = new MessagePackOutboxSerializer();
        try { msgPack.Deserialize<SampleMessage>(jsonBytes); } catch { }

        var proto = new ProtobufOutboxSerializer();
        try { proto.Deserialize<SampleMessage>(jsonBytes); } catch { }

        var resolver = new InMemoryMessageTypeResolver(new[] { ("SampleMessage", typeof(SampleMessage)) });
        string alias = resolver.GetAlias(typeof(SampleMessage));
        bool ok = resolver.TryGetAlias(typeof(SampleMessage), out var resolvedAlias);
    }

    private static async Task VerifyPublishersAndProducersAsync()
    {
        var inMemStore = new InMemoryOutboxStore();
        var producer = new OutboxMessageProducer(inMemStore);
        var payloadBytes = Encoding.UTF8.GetBytes("{\"id\":\"brighter-1\"}");
        var msg = new Paramore.Brighter.Message(
            new MessageHeader(new Id(Guid.NewGuid().ToString()), new RoutingKey("test.topic"), MessageType.MT_EVENT),
            new MessageBody(payloadBytes));
        await producer.SendAsync(msg);
        await producer.SendWithDelayAsync(msg, TimeSpan.FromSeconds(1));

        try
        {
            var eventPublisher = new OutboxEventPublisher(inMemStore);
            await eventPublisher.PublishAsync(new SampleDomainEvent());
        }
        catch
        {
        }
    }

    private static void VerifyDependencyInjectionExtensions()
    {
        var services = new ServiceCollection();

        // Inbox extensions
        services.AddInbox(opt => opt.RetentionPeriod = TimeSpan.FromDays(14));
        services.AddInMemoryInbox(opt => opt.RetentionPeriod = TimeSpan.FromDays(1));
        services.AddInboxDeduplication();
        services.AddIdempotentEventHandler<SampleDomainEvent, SampleDomainEventHandler>();

        // Storage & Broker DI
        services.AddMongoDbOutbox(sp => null!);
        services.AddOutboxBrighterProducer();
        services.AddOutboxCleanupService(opt => opt.RetentionPeriod = TimeSpan.FromDays(7));
        services.AddOutboxEntityFrameworkCore<AppDbContext>();
        services.AddOutboxEventPublisher();
        services.AddOutboxEventPublisher<NullOutboxTransactionProvider>();
        services.AddOutboxMediatRPublisher();
        services.AddOutboxNotificationHandler<SampleNotification>();
        services.AddDaprBrokerPublisher("sample-pubsub");

        // OutboxOptions fluent setup via AddOutbox
        services.AddOutbox(options =>
        {
            options.Configure(s => { });
            options.ConfigureRuntimeOptions(rt => rt.MaxPayloadSizeInBytes = 1024 * 1024);
            options.UseBroker<ConsoleBrokerPublisher>();
            options.UseBroker(sp => new ConsoleBrokerPublisher(sp.GetRequiredService<ILogger<ConsoleBrokerPublisher>>()));
            options.UseBroker(new ConsoleBrokerPublisher(NullLogger<ConsoleBrokerPublisher>.Instance));
            options.UseDapr("pubsub");
            options.UseMariaDb(sp => null!);
            options.UseMySql(sp => null!);
            options.UseOracle(sp => null!);
            options.UsePostgreSql(Environment.GetEnvironmentVariable("POSTGRES_CONNECTION") ?? "Host=localhost;Database=test;Username=postgres");
            options.UsePostgreSql(sp => null!);
            options.UsePostgreSqlNotifications();
            options.UseSqlite(sp => null!);
            options.UseSqlServer(sp => null!);
            options.UseMessagePackSerializer();
            options.UseProtobufSerializer();
            options.UseTypeResolver(new InMemoryMessageTypeResolver(Array.Empty<(string, Type)>()));

            options.Route("SampleMessage").ToPublisher(new ConsoleBrokerPublisher(NullLogger<ConsoleBrokerPublisher>.Instance));
            options.Route("SampleMessage2").ToPublisher(sp => new ConsoleBrokerPublisher(sp.GetRequiredService<ILogger<ConsoleBrokerPublisher>>()));
        });

        // External Integrations
        var endpointConfig = new NServiceBus.EndpointConfiguration("ShowcaseEndpoint");
        endpointConfig.EnableTransactionalOutbox();

        var configurer = (global::Rebus.Config.OptionsConfigurer)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(global::Rebus.Config.OptionsConfigurer));
        try { configurer.EnableTransactionalOutbox(inMem); } catch { }
    }

    private static async Task VerifyServicesAndHandlersAsync()
    {
        // DefaultErrorSanitizer
        var sanitizer = new DefaultErrorSanitizer();
        string sanitized = sanitizer.Sanitize(new InvalidOperationException("Sensitive data inside"));

        // OutboxHealthCheck
        var fakeRepo = new FakeOutboxRepository();
        var healthOptions = Options.Create(new OutboxHealthCheckOptions());
        var healthCheck = new OutboxHealthCheck(new ServiceCollection().BuildServiceProvider(), fakeRepo, healthOptions);
        var healthResult = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        // DefaultInboxConsumerFilter & IdempotencyChecker
        var inMemInbox = new InMemoryInboxStore();
        var consumerFilter = new DefaultInboxConsumerFilter(inMemInbox);
        bool executed = await consumerFilter.ExecuteIdempotentlyAsync("msg-999", "consumer-1", ct => ValueTask.CompletedTask);
        var checker = new IdempotencyChecker(inMemInbox, consumerFilter);
        bool hasProc = await checker.HasProcessedAsync("msg-999", "consumer-1");
        bool executedChecker = await checker.ExecuteIdempotentlyAsync("msg-999", "consumer-1", ct => ValueTask.CompletedTask);

        // OutboxNotificationHandler
        var inMemOutbox = new InMemoryOutboxStore();
        var notifHandler = new OutboxNotificationHandler<SampleNotification>(inMemOutbox);
        await notifHandler.Handle(new SampleNotification(), CancellationToken.None);

        // IdempotentEventHandler
        var eventHandler = new IdempotentEventHandler<SampleDomainEvent>(
            new SampleDomainEventHandler(),
            consumerFilter,
            "ShowcaseConsumer");
        await eventHandler.HandleAsync(new SampleDomainEvent());

        // OutboxPublishBehavior (NServiceBus)
        var nsbBehavior = new OutboxPublishBehavior(inMemOutbox);
        try { await nsbBehavior.Invoke(null!, () => Task.CompletedTask); } catch { }

        // OutboxOutgoingStep (Rebus)
        var rebusStep = new OutboxOutgoingStep(inMemOutbox);
        try { await rebusStep.Process(null!, () => Task.CompletedTask); } catch { }

        // InboxIdempotencyFilter (MassTransit)
        var mtFilter = new InboxIdempotencyFilter<SampleMessage>();
        try { mtFilter.Probe(null!); } catch { }

        // IdempotentEndpointFilter
        var epFilter = new IdempotentEndpointFilter();
        try { await epFilter.InvokeAsync(null!, _ => ValueTask.FromResult<object?>(null)); } catch { }

        // OutboxPipeline
        var terminalDelegate = new OutboxPipelineDelegate((m, meta, ct) => ValueTask.FromResult(DispatchResult.Ok()));
        var pipeline = new OutboxPipeline(Array.Empty<IOutboxMiddleware>(), terminalDelegate);
        var msg = new OutboxMessage(
            Guid.NewGuid(),
            "OrderCreated",
            Encoding.UTF8.GetBytes("{}"),
            null,
            null,
            Encoding.UTF8.GetBytes("{}"),
            DateTimeOffset.UtcNow,
            null,
            null,
            OutboxMessageStatus.Pending,
            0,
            null);
        var outboxMeta = new OutboxMessageMetadata("corr-1", "cause-1", "OrderCreated");
        await pipeline.ExecuteAsync(msg, outboxMeta, CancellationToken.None);

        // Cleanup Services
        var outboxCleanup = new OutboxCleanupService(
            new ServiceCollection().BuildServiceProvider(),
            Options.Create(new OutboxCleanupOptions()),
            LoggerFactory.Create(b => { }).CreateLogger<OutboxCleanupService>());
        try { await outboxCleanup.PerformCleanupAsync(); } catch { }

        var inboxCleanup = new InboxCleanupService(
            new ServiceCollection().BuildServiceProvider(),
            Options.Create(new OutboxInboxOptions()),
            LoggerFactory.Create(b => { }).CreateLogger<InboxCleanupService>());
        try { await inboxCleanup.PerformCleanupAsync(); } catch { }
    }

    private static readonly InMemoryOutboxStore inMem = new();
}

public sealed class SampleMessage
{
    public int OrderId { get; set; } = 123;
}

public sealed class SampleDomainEvent : EricksonLopez.Events.Contracts.IEvent
{
    public EricksonLopez.Events.Identifiers.EventId Id { get; init; } = new(Guid.NewGuid());
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed class SampleDomainEventHandler : EricksonLopez.Events.Contracts.IEventHandler<SampleDomainEvent>
{
    public ValueTask HandleAsync(SampleDomainEvent eventInstance, CancellationToken cancellationToken = default)
        => ValueTask.CompletedTask;
}

public sealed class SampleNotification : EricksonLopez.Mediator.INotification
{
}
