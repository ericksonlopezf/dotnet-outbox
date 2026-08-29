// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using System.Threading.Channels;
using EricksonLopez.Outbox;
using EricksonLopez.Outbox.Diagnostics;
using EricksonLopez.Outbox.Dispatcher;
using EricksonLopez.Outbox.Persistence;
using EricksonLopez.Outbox.Pipeline;
using EricksonLopez.Outbox.Serialization;
using EricksonLopez.Outbox.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

#pragma warning disable CA2012, CS8600, CS8602
namespace EricksonLopez.Outbox.Tests.Delivery;

public partial class OutboxChannelTests
{
    public class DispatchTests
    {
    [Fact]
    public void Constructor_With_HasOnlySingletonMiddlewares_True_Should_PreResolve_Middlewares()
    {
        var publisher = Substitute.For<IBrokerPublisher>();
        var options = Options.Create(new OutboxDispatcherOptions { ChannelCapacity = 10, HasOnlySingletonMiddlewares = true });
        
        var mw = Substitute.For<IOutboxMiddleware>();
        var services = new ServiceCollection().AddSingleton(mw).BuildServiceProvider();
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        var scope = Substitute.For<IServiceScope>();
        scope.ServiceProvider.Returns(services);
        scopeFactory.CreateScope().Returns(scope);

        var channel = new OutboxChannel(NullLogger<OutboxChannel>.Instance, publisher, options, Options.Create(new OutboxRuntimeOptions()), new EricksonLopez.Outbox.Diagnostics.OutboxMetrics(), scopeFactory, FakeErrorSanitizer(), TimeProvider.System);
        
        // Verify that the singleton middlewares were pre-resolved via scopeFactory during construction
        scopeFactory.Received(1).CreateScope();
        channel.Should().NotBeNull();
    }

    [Fact]
    public async Task ProcessMessagesAsync_When_Publisher_Returns_Success_And_ShouldRetry_Should_FailFatal()
    {
        var publisher = Substitute.For<IBrokerPublisher>();
        var logger = new FakeChannelLogger();
        var repo = Substitute.For<IOutboxRepository>();
        var channel = CreateTestChannel(publisher, repo, logger: logger);

        var message = new OutboxMessageTestDataBuilder().WithMessageType("alias").WithPayload(Array.Empty<byte>()).Build();
        await channel.WriteAsync(message, CancellationToken.None);
        CompleteWriter(channel);

        _ = publisher.PublishRawAsync(message, Arg.Any<OutboxMessageMetadata>(), Arg.Any<DispatchContext>())
            .Returns(new ValueTask<DispatchResult>(new DispatchResult { Success = true, ShouldRetry = true }));

        await channel.ProcessMessagesAsync(CancellationToken.None);

        await repo.Received().MarkAsFailedAsync(
            Arg.Any<IReadOnlyList<OutboxMessage>>(),
            Arg.Is<string>(e => e.Contains("Publisher returned Success=true AND ShouldRetry=true")),
            Arg.Any<bool>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessMessagesAsync_Should_Dispatch_Valid_Message()
    {
        var publisher = Substitute.For<IBrokerPublisher>();
        var logger = new FakeChannelLogger();
        var repo = Substitute.For<IOutboxRepository>();
        var channel = CreateTestChannel(publisher, repo, logger: logger);

        var message = new OutboxMessageTestDataBuilder().WithMessageType("alias").WithPayload(Array.Empty<byte>()).WithHeadersJson("{\"traceparent\":\"123\"}").Build();

        await channel.WriteAsync(message, CancellationToken.None);
        CompleteWriter(channel);

        _ = publisher.PublishRawAsync(message, Arg.Any<OutboxMessageMetadata>(), Arg.Any<DispatchContext>())
            .Returns(new ValueTask<DispatchResult>(DispatchResult.Ok()));

        await channel.ProcessMessagesAsync(CancellationToken.None);

        await repo.Received().MarkAsDispatchedAsync(Arg.Any<IReadOnlyList<OutboxMessage>>(), Arg.Any<CancellationToken>());
        logger.LoggedEvents.Should().Contain(e => e.Id == 10000); // MessageDispatched
        logger.LoggedEvents.Should().NotContain(e => e.Id == 10011); // InvalidDispatchResultDetected
    }

    [Fact]
    public async Task ProcessMessagesAsync_Should_Handle_Deserialization_Error()
    {
        var publisher = Substitute.For<IBrokerPublisher>();
        var repo = Substitute.For<IOutboxRepository>();
        var channel = CreateTestChannel(publisher, repo);

        var message = new OutboxMessageTestDataBuilder().WithMessageType("alias").WithPayload(Array.Empty<byte>()).WithHeaders(System.Text.Encoding.UTF8.GetBytes("{ invalid json")).Build();

        await channel.WriteAsync(message, CancellationToken.None);
        CompleteWriter(channel);

        _ = publisher.PublishRawAsync(message, Arg.Any<OutboxMessageMetadata>(), Arg.Any<DispatchContext>())
            .Returns(new ValueTask<DispatchResult>(DispatchResult.Ok()));

        await channel.ProcessMessagesAsync(CancellationToken.None);

        await repo.Received().MarkAsFailedAsync(Arg.Any<IReadOnlyList<OutboxMessage>>(), Arg.Any<string>(), true, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessMessagesAsync_Should_Handle_Failure_Retry()
    {
        var publisher = Substitute.For<IBrokerPublisher>();
        var repo = Substitute.For<IOutboxRepository>();
        var channel = CreateTestChannel(publisher, repo);

        var message = new OutboxMessageTestDataBuilder().WithMessageType("alias").WithPayload(Array.Empty<byte>()).Build();

        await channel.WriteAsync(message, CancellationToken.None);
        CompleteWriter(channel);

        _ = publisher.PublishRawAsync(message, Arg.Any<OutboxMessageMetadata>(), Arg.Any<DispatchContext>())
            .Returns(new ValueTask<DispatchResult>(DispatchResult.FailAndRetry(new InvalidOperationException("test"))));

        await channel.ProcessMessagesAsync(CancellationToken.None);

        await repo.Received().MarkAsFailedAsync(Arg.Any<IReadOnlyList<OutboxMessage>>(), "test", false, Arg.Any<CancellationToken>());
        await repo.DidNotReceiveWithAnyArgs().MarkAsDispatchedAsync(default!, default!);
    }

    [Fact]
    public async Task ProcessMessagesAsync_Should_Handle_Fatal_Failure()
    {
        var publisher = Substitute.For<IBrokerPublisher>();
        var logger = new FakeChannelLogger();
        var repo = Substitute.For<IOutboxRepository>();
        var dlqRepo = Substitute.For<IDeadLetterRepository>();
        var channel = CreateTestChannel(publisher, repo, dlqRepo, logger: logger);

        var message = new OutboxMessageTestDataBuilder().WithMessageType("alias").WithPayload(Array.Empty<byte>()).Build();

        await channel.WriteAsync(message, CancellationToken.None);
        CompleteWriter(channel);

        _ = publisher.PublishRawAsync(message, Arg.Any<OutboxMessageMetadata>(), Arg.Any<DispatchContext>())
            .Returns(new ValueTask<DispatchResult>(DispatchResult.FailFatal(new InvalidOperationException("fatal"))));

        await channel.ProcessMessagesAsync(CancellationToken.None);

        await dlqRepo.Received().InsertAsync(Arg.Any<DeadLetterMessage>(), Arg.Any<EricksonLopez.Outbox.Persistence.IOutboxTransactionContext>(), Arg.Any<CancellationToken>());
        await repo.Received().MarkAsFailedAsync(Arg.Any<IReadOnlyList<OutboxMessage>>(), "fatal", true, Arg.Any<CancellationToken>());
        await repo.DidNotReceiveWithAnyArgs().MarkAsDispatchedAsync(default!, default!);
        logger.LoggedEvents.Should().Contain(e => e.Id == 10002); // MessageDeadLettered
        logger.LoggedEvents.Should().NotContain(e => e.Id == 10011); // InvalidDispatchResultDetected
    }

    [Fact]
    public async Task ProcessMessagesAsync_Should_Truncate_Large_Headers()
    {
        var publisher = Substitute.For<IBrokerPublisher>();
        var repo = Substitute.For<IOutboxRepository>();
        var channel = CreateTestChannel(publisher, repo);

        var message = new OutboxMessageTestDataBuilder().WithMessageType("alias").WithPayload(Array.Empty<byte>()).WithHeaders(System.Text.Encoding.UTF8.GetBytes(new string('a', 1024 * 1025))).Build();

        await channel.WriteAsync(message, CancellationToken.None);
        CompleteWriter(channel);

        _ = publisher.PublishRawAsync(message, Arg.Any<OutboxMessageMetadata>(), Arg.Any<DispatchContext>())
            .Returns(new ValueTask<DispatchResult>(DispatchResult.Ok()));

        await channel.ProcessMessagesAsync(CancellationToken.None);

        await repo.Received().MarkAsFailedAsync(Arg.Any<IReadOnlyList<OutboxMessage>>(), Arg.Any<string>(), true, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Channel_Records_FillRatio_Metric()
    {
        var optionsMock = Options.Create(new OutboxDispatcherOptions { ChannelCapacity = 10 });
        using var listener = new System.Diagnostics.Metrics.MeterListener();
        listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Name == "messaging.outbox.channel.fill_ratio")
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };

        double recordedValue = -1;
        listener.SetMeasurementEventCallback<double>((instrument, measurement, tags, state) =>
        {
            if (instrument.Name == "messaging.outbox.channel.fill_ratio")
            {
                recordedValue = measurement;
            }
        });

        listener.Start();

        var channel = new OutboxChannel(Microsoft.Extensions.Logging.Abstractions.NullLogger<OutboxChannel>.Instance, NSubstitute.Substitute.For<EricksonLopez.Outbox.IBrokerPublisher>(), optionsMock, Microsoft.Extensions.Options.Options.Create(new EricksonLopez.Outbox.OutboxRuntimeOptions()), new EricksonLopez.Outbox.Diagnostics.OutboxMetrics(), FakeScopeFactory(new ServiceCollection().BuildServiceProvider()), FakeErrorSanitizer(), TimeProvider.System);

        // Force the ObservableGauge to be evaluated
        listener.RecordObservableInstruments();

        recordedValue.Should().Be(0.0);
    }

    [Fact]
    public async Task ProcessMessagesAsync_Should_Handle_Fatal_Failure_Without_Dlq()
    {
        var publisher = Substitute.For<IBrokerPublisher>();
        var repo = Substitute.For<IOutboxRepository>();
        var channel = CreateTestChannel(publisher, repo);

        var message = new OutboxMessageTestDataBuilder().WithMessageType("alias").WithPayload(Array.Empty<byte>()).Build();

        await channel.WriteAsync(message, CancellationToken.None);
        CompleteWriter(channel);

        _ = publisher.PublishRawAsync(message, Arg.Any<OutboxMessageMetadata>(), Arg.Any<DispatchContext>())
            .Returns(new ValueTask<DispatchResult>(DispatchResult.FailFatal(new InvalidOperationException("fatal"))));

        await channel.ProcessMessagesAsync(CancellationToken.None);

        await repo.Received().MarkAsFailedAsync(Arg.Any<IReadOnlyList<OutboxMessage>>(), "fatal", true, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessMessagesAsync_Should_Handle_Dlq_Exception()
    {
        var publisher = Substitute.For<IBrokerPublisher>();
        var logger = new FakeChannelLogger();
        var repo = Substitute.For<IOutboxRepository>();
        var dlqRepo = Substitute.For<IDeadLetterRepository>();
        
        dlqRepo.InsertAsync(Arg.Any<DeadLetterMessage>(), Arg.Any<EricksonLopez.Outbox.Persistence.IOutboxTransactionContext>(), Arg.Any<CancellationToken>())
            .Returns(x => throw new InvalidOperationException("DB Down"));

        var channel = CreateTestChannel(publisher, repo, dlqRepo, logger: logger);

        var message = new OutboxMessage(Guid.NewGuid(), "alias", Array.Empty<byte>(), null, null, System.Text.Encoding.UTF8.GetBytes("{}"), DateTimeOffset.UtcNow, null, null, 0, 0, null);

        await channel.WriteAsync(message, CancellationToken.None);
        CompleteWriter(channel);

        _ = publisher.PublishRawAsync(message, Arg.Any<OutboxMessageMetadata>(), Arg.Any<DispatchContext>())
            .Returns(new ValueTask<DispatchResult>(DispatchResult.FailFatal(new InvalidOperationException("fatal"))));

        await channel.ProcessMessagesAsync(CancellationToken.None);

        // P0-FIX: When DLQ INSERT fails, the message MUST still be marked as dead-lettered (isDeadLetter=true)
        // to prevent an infinite retry loop. Previously, a failed DLQ INSERT caused isDeadLetter=false,
        // which left the message in state=3 (Failed) with retry_count >= MaxRetryCount, causing the poller
        // to re-fetch it indefinitely.
        await repo.Received().MarkAsFailedAsync(Arg.Any<IReadOnlyList<OutboxMessage>>(), "fatal", true, Arg.Any<CancellationToken>());
        await repo.DidNotReceive().MarkAsDispatchedAsync(Arg.Any<IReadOnlyList<OutboxMessage>>(), Arg.Any<CancellationToken>());
        logger.LoggedEvents.Should().Contain(e => e.Id == 10003); // DlqInsertFailed
    }

    [Fact]
    public async Task ProcessMessagesAsync_Should_Exit_Naturally_When_Writer_Completed()
    {
        var publisher = Substitute.For<IBrokerPublisher>();
        var repo = Substitute.For<IOutboxRepository>();
        var channel = CreateTestChannel(publisher, repo);

        // Complete the channel so WaitToReadAsync returns false
        CompleteWriter(channel);

        // Pass a token that is NOT cancelled, so it exits naturally
        var act = async () => await channel.ProcessMessagesAsync(CancellationToken.None);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ProcessMessagesAsync_Should_Handle_Cancellation_During_MicroBatching()
    {
        var publisher = Substitute.For<IBrokerPublisher>();
        var repo = Substitute.For<IOutboxRepository>();
        var channel = CreateTestChannel(publisher, repo);

        var message = new OutboxMessage(Guid.NewGuid(), "alias", Array.Empty<byte>(), null, null, System.Text.Encoding.UTF8.GetBytes("{}"), DateTimeOffset.UtcNow, null, null, 0, 0, null);

        await channel.WriteAsync(message, CancellationToken.None);
        
        publisher.PublishRawAsync(message, Arg.Any<OutboxMessageMetadata>(), Arg.Any<DispatchContext>())
            .Returns(_ => {
                CompleteWriter(channel);
                return new ValueTask<DispatchResult>(DispatchResult.Ok());
            });

        await channel.ProcessMessagesAsync(CancellationToken.None);

        await repo.Received().MarkAsDispatchedAsync(Arg.Any<IReadOnlyList<OutboxMessage>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessMessagesAsync_Should_Use_Correct_Attempt_Number()
    {
        var publisher = Substitute.For<IBrokerPublisher>();
        var repo = Substitute.For<IOutboxRepository>();
        var channel = CreateTestChannel(publisher, repo);

        var message = new OutboxMessage(Guid.NewGuid(), "alias", Array.Empty<byte>(), null, null, System.Text.Encoding.UTF8.GetBytes("{}"), DateTimeOffset.UtcNow, null, null, OutboxMessageStatus.Pending, 2, null);

        await channel.WriteAsync(message, CancellationToken.None);
        CompleteWriter(channel);

        publisher.PublishRawAsync(message, Arg.Any<OutboxMessageMetadata>(), Arg.Is<DispatchContext>(c => c.Attempt == 3))
            .Returns(new ValueTask<DispatchResult>(DispatchResult.Ok()));

        await channel.ProcessMessagesAsync(CancellationToken.None);

        await publisher.Received().PublishRawAsync(message, Arg.Any<OutboxMessageMetadata>(), Arg.Is<DispatchContext>(c => c.Attempt == 3));
    }

    [Fact]
    public async Task ProcessMessagesAsync_Should_Log_Warning_On_Default_DispatchResult()
    {
        var publisher = Substitute.For<IBrokerPublisher>();
        var logger = new FakeChannelLogger();
        var repo = Substitute.For<IOutboxRepository>();
        var channel = CreateTestChannel(publisher, repo, logger: logger);

        var message = new OutboxMessage(Guid.NewGuid(), "alias", Array.Empty<byte>(), null, null, System.Text.Encoding.UTF8.GetBytes("{}"), DateTimeOffset.UtcNow, null, null, 0, 0, null);

        await channel.WriteAsync(message, CancellationToken.None);
        CompleteWriter(channel);

        // Return default DispatchResult
        publisher.PublishRawAsync(message, Arg.Any<OutboxMessageMetadata>(), Arg.Any<DispatchContext>())
            .Returns(new ValueTask<DispatchResult>(default(DispatchResult)));

        await channel.ProcessMessagesAsync(CancellationToken.None);

        // It should be treated as fatal failure since Success is false, ShouldRetry is false, and Error is null
        await repo.Received().MarkAsFailedAsync(Arg.Any<IReadOnlyList<OutboxMessage>>(), "IBrokerPublisher returned default(DispatchResult) for alias.", true, Arg.Any<CancellationToken>());
        logger.LoggedEvents.Should().Contain(e => e.Id == 10011); // InvalidDispatchResultDetected
    }

    [Fact]
    public async Task ProcessMessagesAsync_Should_Handle_NoRetry_Increment()
    {
        var publisher = Substitute.For<IBrokerPublisher>();
        var logger = new FakeChannelLogger();
        var repo = Substitute.For<IOutboxRepository>();
        var channel = CreateTestChannel(publisher, repo, logger: logger);

        var message = new OutboxMessage(Guid.NewGuid(), "alias", Array.Empty<byte>(), null, null, System.Text.Encoding.UTF8.GetBytes("{}"), DateTimeOffset.UtcNow, null, null, 0, 0, null);

        await channel.WriteAsync(message, CancellationToken.None);
        CompleteWriter(channel);

        // Return a DispatchResult that says ShouldRetry but IncrementRetryCount = false
        publisher.PublishRawAsync(message, Arg.Any<OutboxMessageMetadata>(), Arg.Any<DispatchContext>())
            .Returns(new ValueTask<DispatchResult>(new DispatchResult(false, true, new InvalidOperationException("Circuit Breaker Open"), false)));

        await channel.ProcessMessagesAsync(CancellationToken.None);

        // MarkAsFailedAsync should NOT be called because we are skipping it
        await repo.DidNotReceiveWithAnyArgs().MarkAsFailedAsync(default!, default!, default!, default!);
        logger.LoggedEvents.Should().Contain(e => e.Id == 10009); // MessageDelayedNoRetry
    }

    [Fact]
    public void ProcessMessagesAsync_Should_Handle_Null_Options_In_Constructor()
    {
        var publisher = Substitute.For<IBrokerPublisher>();
        var channel = new OutboxChannel(NullLogger<OutboxChannel>.Instance, publisher, null!, null!, new EricksonLopez.Outbox.Diagnostics.OutboxMetrics(), FakeScopeFactory(new ServiceCollection().BuildServiceProvider()), FakeErrorSanitizer(), TimeProvider.System);
        
        channel.Should().NotBeNull();
    }
    
    [Fact]
    public async Task ProcessMessagesAsync_Should_DeadLetter_When_Max_Retries_Reached()
    {
        var publisher = Substitute.For<IBrokerPublisher>();
        var repo = Substitute.For<IOutboxRepository>();
        var channel = CreateTestChannel(publisher, repo, dispatcherOptions: new OutboxDispatcherOptions { ChannelCapacity = 10, MaxRetryCount = 3 });

        // Already at retry count 2. Attempt 3 (RetryCount + 1) will be the final one and should DeadLetter
        var message = new OutboxMessage(Guid.NewGuid(), "alias", Array.Empty<byte>(), null, null, System.Text.Encoding.UTF8.GetBytes("{}"), DateTimeOffset.UtcNow, null, null, OutboxMessageStatus.Pending, 2, null);

        await channel.WriteAsync(message, CancellationToken.None);
        CompleteWriter(channel);

        publisher.PublishRawAsync(message, Arg.Any<OutboxMessageMetadata>(), Arg.Any<DispatchContext>())
            .Returns(new ValueTask<DispatchResult>(DispatchResult.FailAndRetry(new InvalidOperationException("Fail"))));

        await channel.ProcessMessagesAsync(CancellationToken.None);

        await repo.Received().MarkAsFailedAsync(Arg.Any<IReadOnlyList<OutboxMessage>>(), "Fail", true, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessMessagesAsync_Should_Retry_Db_Operations()
    {
        var publisher = Substitute.For<IBrokerPublisher>();
        var logger = new FakeChannelLogger();
        var repo = Substitute.For<IOutboxRepository>();
        var channel = CreateTestChannel(publisher, repo, logger: logger, dispatcherOptions: new OutboxDispatcherOptions { ChannelCapacity = 10, DbRetryBaseDelayMs = 1 });

        var message = new OutboxMessage(Guid.NewGuid(), "alias", Array.Empty<byte>(), null, null, System.Text.Encoding.UTF8.GetBytes("{}"), DateTimeOffset.UtcNow, null, null, 0, 0, null);

        await channel.WriteAsync(message, CancellationToken.None);
        CompleteWriter(channel);

        publisher.PublishRawAsync(message, Arg.Any<OutboxMessageMetadata>(), Arg.Any<DispatchContext>())
            .Returns(new ValueTask<DispatchResult>(DispatchResult.Ok()));

        int callCount = 0;
        repo.MarkAsDispatchedAsync(Arg.Any<IReadOnlyList<OutboxMessage>>(), Arg.Any<CancellationToken>())
            .Returns(x => 
            {
                callCount++;
                if (callCount < 2) throw new InvalidOperationException("DB Transient Error");
                return ValueTask.CompletedTask;
            });

        await channel.ProcessMessagesAsync(CancellationToken.None);

        callCount.Should().Be(2); // Retried once and succeeded
        logger.LoggedEvents.Should().Contain(e => e.Id == 10010); // DbRetryAttempt
    }

    [Fact]
    public async Task ProcessMessagesAsync_Should_Fail_After_Max_Db_Retries()
    {
        var publisher = Substitute.For<IBrokerPublisher>();
        var logger = new FakeChannelLogger();
        var repo = Substitute.For<IOutboxRepository>();
        var channel = CreateTestChannel(publisher, repo, logger: logger, dispatcherOptions: new OutboxDispatcherOptions { ChannelCapacity = 10, DbRetryBaseDelayMs = 1 });

        var message = new OutboxMessage(Guid.NewGuid(), "alias", Array.Empty<byte>(), null, null, System.Text.Encoding.UTF8.GetBytes("{}"), DateTimeOffset.UtcNow, null, null, 0, 0, null);

        await channel.WriteAsync(message, CancellationToken.None);
        CompleteWriter(channel);

        publisher.PublishRawAsync(message, Arg.Any<OutboxMessageMetadata>(), Arg.Any<DispatchContext>())
            .Returns(new ValueTask<DispatchResult>(DispatchResult.Ok()));

        int callCount = 0;
        repo.MarkAsDispatchedAsync(Arg.Any<IReadOnlyList<OutboxMessage>>(), Arg.Any<CancellationToken>())
            .Returns(x => 
            {
                callCount++;
                throw new InvalidOperationException("DB Permanent Error");
            });

        // The method catches and throws if it exhausts retries. 
        // 1 initial attempt + 3 retries = 4 attempts total.
        await Assert.ThrowsAsync<InvalidOperationException>(() => channel.ProcessMessagesAsync(CancellationToken.None));

        callCount.Should().Be(4); // 1 initial + 3 retries. This kills the mutant `attempt <= maxAttempts` because it would do 5 calls!
    }

    [Fact]
    public async Task ProcessMessagesAsync_Should_Log_Cancellation()
    {
        var publisher = Substitute.For<IBrokerPublisher>();
        var logger = new FakeChannelLogger();
        var repo = Substitute.For<IOutboxRepository>();
        var channel = CreateTestChannel(publisher, repo, logger: logger);

        var cts = new CancellationTokenSource();
        cts.Cancel();

        await channel.ProcessMessagesAsync(cts.Token);
        
        logger.LoggedEvents.Should().Contain(e => e.Id == 10005); // ChannelCancelled
    }

    [Fact]
    public void Constructor_WhenLoggerIsNull_ThrowsArgumentNullException()
    {
        var publisher = Substitute.For<IBrokerPublisher>();
        var options = Microsoft.Extensions.Options.Options.Create(new OutboxDispatcherOptions());
        var runtimeOptions = Microsoft.Extensions.Options.Options.Create(new OutboxRuntimeOptions());
        var metrics = new EricksonLopez.Outbox.Diagnostics.OutboxMetrics();
        var scopeFactory = Substitute.For<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>();
        var errorSanitizer = FakeErrorSanitizer();

        Assert.Throws<ArgumentNullException>(() => new OutboxChannel(null!, publisher, options, runtimeOptions, metrics, scopeFactory, errorSanitizer, TimeProvider.System));
    }

    [Fact]
    public void Constructor_WhenPublisherIsNull_ThrowsArgumentNullException()
    {
        var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<OutboxChannel>.Instance;
        var options = Microsoft.Extensions.Options.Options.Create(new OutboxDispatcherOptions());
        var runtimeOptions = Microsoft.Extensions.Options.Options.Create(new OutboxRuntimeOptions());
        var metrics = new EricksonLopez.Outbox.Diagnostics.OutboxMetrics();
        var scopeFactory = Substitute.For<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>();
        var errorSanitizer = FakeErrorSanitizer();

        Assert.Throws<ArgumentNullException>(() => new OutboxChannel(logger, null!, options, runtimeOptions, metrics, scopeFactory, errorSanitizer, TimeProvider.System));
    }

    [Fact]
    public void Constructor_WhenMetricsIsNull_ThrowsArgumentNullException()
    {
        var publisher = Substitute.For<IBrokerPublisher>();
        var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<OutboxChannel>.Instance;
        var options = Microsoft.Extensions.Options.Options.Create(new OutboxDispatcherOptions());
        var runtimeOptions = Microsoft.Extensions.Options.Options.Create(new OutboxRuntimeOptions());
        var scopeFactory = Substitute.For<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>();
        var errorSanitizer = FakeErrorSanitizer();

        Assert.Throws<ArgumentNullException>(() => new OutboxChannel(logger, publisher, options, runtimeOptions, null!, scopeFactory, errorSanitizer, TimeProvider.System));
    }

    [Fact]
    public void Constructor_WhenScopeFactoryIsNull_ThrowsArgumentNullException()
    {
        var publisher = Substitute.For<IBrokerPublisher>();
        var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<OutboxChannel>.Instance;
        var options = Microsoft.Extensions.Options.Options.Create(new OutboxDispatcherOptions());
        var runtimeOptions = Microsoft.Extensions.Options.Options.Create(new OutboxRuntimeOptions());
        var metrics = new EricksonLopez.Outbox.Diagnostics.OutboxMetrics();
        var errorSanitizer = FakeErrorSanitizer();

        Assert.Throws<ArgumentNullException>(() => new OutboxChannel(logger, publisher, options, runtimeOptions, metrics, null!, errorSanitizer, TimeProvider.System));
    }

    [Fact]
    public void Constructor_WhenErrorSanitizerIsNull_ThrowsArgumentNullException()
    {
        var publisher = Substitute.For<IBrokerPublisher>();
        var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<OutboxChannel>.Instance;
        var options = Microsoft.Extensions.Options.Options.Create(new OutboxDispatcherOptions());
        var runtimeOptions = Microsoft.Extensions.Options.Options.Create(new OutboxRuntimeOptions());
        var metrics = new EricksonLopez.Outbox.Diagnostics.OutboxMetrics();
        var scopeFactory = Substitute.For<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>();

        Assert.Throws<ArgumentNullException>(() => new OutboxChannel(logger, publisher, options, runtimeOptions, metrics, scopeFactory, null!, TimeProvider.System));
    }

    [Fact]
    public void Constructor_WhenTimeProviderIsNull_ThrowsArgumentNullException()
    {
        var publisher = Substitute.For<IBrokerPublisher>();
        var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<OutboxChannel>.Instance;
        var options = Microsoft.Extensions.Options.Options.Create(new OutboxDispatcherOptions());
        var runtimeOptions = Microsoft.Extensions.Options.Options.Create(new OutboxRuntimeOptions());
        var metrics = new EricksonLopez.Outbox.Diagnostics.OutboxMetrics();
        var scopeFactory = Substitute.For<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>();
        var errorSanitizer = Substitute.For<EricksonLopez.Outbox.Diagnostics.IErrorSanitizer>();

        Assert.Throws<ArgumentNullException>(() => new OutboxChannel(logger, publisher, options, runtimeOptions, metrics, scopeFactory, errorSanitizer, null!));
    }

    [Fact]
    public async Task ProcessMessagesAsync_Should_Handle_Malformed_Json_Headers()
    {
        var repo = Substitute.For<IOutboxRepository>();
        var publisher = Substitute.For<IBrokerPublisher>();
        var channel = CreateTestChannel(publisher, repo);

        var msg = new OutboxMessage(Guid.NewGuid(), "type", Array.Empty<byte>(), null, null, System.Text.Encoding.UTF8.GetBytes("{ \"key\" "), DateTimeOffset.UtcNow, null, null, OutboxMessageStatus.Pending, 0, null);
        await channel.WriteAsync(msg, CancellationToken.None);
        CompleteWriter(channel);
        
        await channel.ProcessMessagesAsync(CancellationToken.None);

        await repo.Received(1).MarkAsFailedAsync(
            Arg.Is<IReadOnlyList<OutboxMessage>>(l => l.Count == 1 && l[0].Id == msg.Id),
            Arg.Is<string>(s => s.Contains("Failed to deserialize headers")),
            Arg.Is(true),
            Arg.Any<CancellationToken>());
        await publisher.DidNotReceiveWithAnyArgs().PublishRawAsync(default!, default!, default!);
    }

    [Fact]
    public async Task ProcessMessagesAsync_Should_Handle_Json_Null_Header_Value()
    {
        var repo = Substitute.For<IOutboxRepository>();
        var publisher = Substitute.For<IBrokerPublisher>();
        var msg = new OutboxMessage(Guid.NewGuid(), "type", Array.Empty<byte>(), null, null, System.Text.Encoding.UTF8.GetBytes("{ \"key\": null }"), DateTimeOffset.UtcNow, null, null, OutboxMessageStatus.Pending, 0, null);

        publisher.PublishRawAsync(msg, Arg.Any<OutboxMessageMetadata>(), Arg.Any<DispatchContext>())
            .Returns(new ValueTask<DispatchResult>(DispatchResult.Ok()));

        var channel = CreateTestChannel(publisher, repo);

        await channel.WriteAsync(msg, CancellationToken.None);
        CompleteWriter(channel);
        
        await channel.ProcessMessagesAsync(CancellationToken.None);

        await publisher.Received(1).PublishRawAsync(
            Arg.Is<OutboxMessage>(m => m.Id == msg.Id),
            Arg.Any<OutboxMessageMetadata>(),
            Arg.Any<DispatchContext>());
        await repo.Received(1).MarkAsDispatchedAsync(
            Arg.Is<IReadOnlyList<OutboxMessage>>(l => l.Count == 1 && l[0].Id == msg.Id),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessMessagesAsync_Should_Handle_Headers_Cache_Miss_Swap()
    {
        var repo = Substitute.For<IOutboxRepository>();
        var publisher = Substitute.For<IBrokerPublisher>();
        publisher.PublishRawAsync(Arg.Any<OutboxMessage>(), Arg.Any<OutboxMessageMetadata>(), Arg.Any<DispatchContext>())
            .Returns(new ValueTask<DispatchResult>(DispatchResult.Ok()));

        var channel = CreateTestChannel(publisher, repo);

        var msg1 = new OutboxMessage(Guid.NewGuid(), "type", Array.Empty<byte>(), null, null, System.Text.Encoding.UTF8.GetBytes("{ \"key1\": \"v1\" }"), DateTimeOffset.UtcNow, null, null, OutboxMessageStatus.Pending, 0, null);
        var msg2 = new OutboxMessage(Guid.NewGuid(), "type", Array.Empty<byte>(), null, null, System.Text.Encoding.UTF8.GetBytes("{ \"key2\": \"v2\" }"), DateTimeOffset.UtcNow, null, null, OutboxMessageStatus.Pending, 0, null);
        await channel.WriteAsync(msg1, CancellationToken.None);
        await channel.WriteAsync(msg2, CancellationToken.None);
        CompleteWriter(channel);
        
        await channel.ProcessMessagesAsync(CancellationToken.None);

        await publisher.Received(2).PublishRawAsync(
            Arg.Any<OutboxMessage>(),
            Arg.Any<OutboxMessageMetadata>(),
            Arg.Any<DispatchContext>());
        await repo.Received(1).MarkAsDispatchedAsync(
            Arg.Is<IReadOnlyList<OutboxMessage>>(l => l.Count == 2),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessMessagesAsync_Should_Skip_MessageTypeTag_If_Disabled()
    {
        var repo = Substitute.For<IOutboxRepository>();
        var msg = new OutboxMessage(Guid.NewGuid(), "type", Array.Empty<byte>(), null, null, Array.Empty<byte>(), DateTimeOffset.UtcNow, null, null, OutboxMessageStatus.Pending, 0, null);

        var publisher = Substitute.For<IBrokerPublisher>();
        publisher.PublishRawAsync(msg, Arg.Any<OutboxMessageMetadata>(), Arg.Any<DispatchContext>())
            .Returns(new ValueTask<DispatchResult>(DispatchResult.FailFatal(new InvalidOperationException("fatal error"))));

        var channel = CreateTestChannel(publisher, repo, runtimeOptions: new OutboxRuntimeOptions { IncludeMessageTypeTag = false });

        await channel.WriteAsync(msg, CancellationToken.None);
        CompleteWriter(channel);
        
        await channel.ProcessMessagesAsync(CancellationToken.None);
        
        // Assert: It should handle the fatal failure without throwing, 
        // and the metric recording should have skipped the message type tag branch.
        await repo.Received().MarkAsFailedAsync(Arg.Any<IReadOnlyList<OutboxMessage>>(), Arg.Any<string>(), true, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessMessagesAsync_Should_Handle_NonObject_Json_Headers()
    {
        var repo = Substitute.For<IOutboxRepository>();

        // A JSON array "[]" instead of an object "{}"
        var msg1 = new OutboxMessage(Guid.NewGuid(), "type", Array.Empty<byte>(), null, null, System.Text.Encoding.UTF8.GetBytes("[]"), DateTimeOffset.UtcNow, null, null, OutboxMessageStatus.Pending, 0, null);
        // Only whitespace so reader.Read() is false
        var msg2 = new OutboxMessage(Guid.NewGuid(), "type", Array.Empty<byte>(), null, null, System.Text.Encoding.UTF8.GetBytes("   "), DateTimeOffset.UtcNow, null, null, OutboxMessageStatus.Pending, 0, null);

        var publisher = Substitute.For<IBrokerPublisher>();
        publisher.PublishRawAsync(Arg.Any<OutboxMessage>(), Arg.Any<OutboxMessageMetadata>(), Arg.Any<DispatchContext>())
            .Returns(new ValueTask<DispatchResult>(DispatchResult.Ok()));

        var channel = CreateTestChannel(publisher, repo);

        await channel.WriteAsync(msg1, CancellationToken.None);
        await channel.WriteAsync(msg2, CancellationToken.None);
        CompleteWriter(channel);
        
        await channel.ProcessMessagesAsync(CancellationToken.None);
        
        await repo.Received().MarkAsDispatchedAsync(Arg.Any<IReadOnlyList<OutboxMessage>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessMessagesAsync_Should_Truncate_Large_Payload()
    {
        var publisher = Substitute.For<IBrokerPublisher>();
        var repo = Substitute.For<IOutboxRepository>();
        var channel = CreateTestChannel(publisher, repo, runtimeOptions: new OutboxRuntimeOptions { MaxPayloadSizeInBytes = 100 });

        var message = new OutboxMessage(Guid.NewGuid(), "alias", new byte[101], null, null, System.Text.Encoding.UTF8.GetBytes("{}"), DateTimeOffset.UtcNow, null, null, 0, 0, null);

        await channel.WriteAsync(message, CancellationToken.None);
        CompleteWriter(channel);
        await channel.ProcessMessagesAsync(CancellationToken.None);

        await repo.Received().MarkAsFailedAsync(Arg.Any<IReadOnlyList<OutboxMessage>>(), Arg.Any<string>(), true, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessMessagesAsync_Should_Use_Header_Cache_For_Same_Headers()
    {
        var publisher = Substitute.For<IBrokerPublisher>();
        var repo = Substitute.For<IOutboxRepository>();
        var channel = CreateTestChannel(publisher, repo);

        var headers = System.Text.Encoding.UTF8.GetBytes("{\"traceparent\":\"123\"}");
        var msg1 = new OutboxMessage(Guid.NewGuid(), "alias", Array.Empty<byte>(), null, null, headers, DateTimeOffset.UtcNow, null, null, 0, 0, null);
        var msg2 = new OutboxMessage(Guid.NewGuid(), "alias", Array.Empty<byte>(), null, null, headers, DateTimeOffset.UtcNow, null, null, 0, 0, null);

        await channel.WriteAsync(msg1, CancellationToken.None);
        await channel.WriteAsync(msg2, CancellationToken.None);
        CompleteWriter(channel);

        publisher.PublishRawAsync(default!, default!, default!)
            .ReturnsForAnyArgs(new ValueTask<DispatchResult>(DispatchResult.Ok()));

        await channel.ProcessMessagesAsync(CancellationToken.None);

        await repo.Received().MarkAsDispatchedAsync(Arg.Is<IReadOnlyList<OutboxMessage>>(l => l.Count >= 1), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessMessagesAsync_Should_Handle_Partial_Batch_Success()
    {
        var publisher = Substitute.For<IBrokerPublisher>();
        var repo = Substitute.For<IOutboxRepository>();
        var channel = CreateTestChannel(publisher, repo);

        var msg1 = new OutboxMessage(Guid.NewGuid(), "alias1", Array.Empty<byte>(), null, null, System.Text.Encoding.UTF8.GetBytes("{}"), DateTimeOffset.UtcNow, null, null, 0, 0, null);
        var msg2 = new OutboxMessage(Guid.NewGuid(), "alias2", Array.Empty<byte>(), null, null, System.Text.Encoding.UTF8.GetBytes("{}"), DateTimeOffset.UtcNow, null, null, 0, 0, null);

        await channel.WriteAsync(msg1, CancellationToken.None);
        await channel.WriteAsync(msg2, CancellationToken.None);
        CompleteWriter(channel);

        publisher.PublishRawAsync(Arg.Is<OutboxMessage>(m => m.Id == msg1.Id), Arg.Any<OutboxMessageMetadata>(), Arg.Any<DispatchContext>())
            .Returns(new ValueTask<DispatchResult>(DispatchResult.Ok()));
            
        publisher.PublishRawAsync(Arg.Is<OutboxMessage>(m => m.Id == msg2.Id), Arg.Any<OutboxMessageMetadata>(), Arg.Any<DispatchContext>())
            .Returns(new ValueTask<DispatchResult>(DispatchResult.FailAndRetry(new InvalidOperationException("test"))));

        await channel.ProcessMessagesAsync(CancellationToken.None);

        await repo.Received().MarkAsDispatchedAsync(Arg.Is<IReadOnlyList<OutboxMessage>>(l => l.Count == 1 && l[0].Id == msg1.Id), Arg.Any<CancellationToken>());
        await repo.Received().MarkAsFailedAsync(Arg.Any<IReadOnlyList<OutboxMessage>>(), Arg.Any<string>(), false, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessMessagesAsync_Should_Cancel_During_Batch_Iteration()
    {
        var publisher = Substitute.For<IBrokerPublisher>();
        var repo = Substitute.For<IOutboxRepository>();
        var channel = CreateTestChannel(publisher, repo);

        var msg1 = new OutboxMessage(Guid.NewGuid(), "alias1", Array.Empty<byte>(), null, null, System.Text.Encoding.UTF8.GetBytes("{}"), DateTimeOffset.UtcNow, null, null, 0, 0, null);
        var msg2 = new OutboxMessage(Guid.NewGuid(), "alias2", Array.Empty<byte>(), null, null, System.Text.Encoding.UTF8.GetBytes("{}"), DateTimeOffset.UtcNow, null, null, 0, 0, null);

        await channel.WriteAsync(msg1, CancellationToken.None);
        await channel.WriteAsync(msg2, CancellationToken.None);
        
        var cts = new CancellationTokenSource();

        publisher.PublishRawAsync(Arg.Is<OutboxMessage>(m => m.Id == msg1.Id), Arg.Any<OutboxMessageMetadata>(), Arg.Any<DispatchContext>())
            .Returns(x => 
            {
                cts.Cancel();
                return new ValueTask<DispatchResult>(DispatchResult.Ok());
            });

        await channel.ProcessMessagesAsync(cts.Token);

        await publisher.DidNotReceive().PublishRawAsync(Arg.Is<OutboxMessage>(m => m.Id == msg2.Id), Arg.Any<OutboxMessageMetadata>(), Arg.Any<DispatchContext>());
        await repo.Received().MarkAsDispatchedAsync(Arg.Is<IReadOnlyList<OutboxMessage>>(l => l.Count == 1 && l[0].Id == msg1.Id), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessMessagesAsync_WhenChannelCapacityReachedAndPublisherRecovers_ProcessesAllMessagesUnderBackpressure()
    {
        // Arrange: small bounded capacity to trigger backpressure
        var publisher = Substitute.For<IBrokerPublisher>();
        var repo = Substitute.For<IOutboxRepository>();
        var options = new OutboxDispatcherOptions { ChannelCapacity = 2, BatchSize = 2 };
        var channel = CreateTestChannel(publisher, repo, dispatcherOptions: options);

        var messages = Enumerable.Range(0, 6)
            .Select(i => new OutboxMessageTestDataBuilder().WithMessageType($"type-{i}").Build())
            .ToList();

        var dispatchedList = new System.Collections.Concurrent.ConcurrentBag<Guid>();
        int callCount = 0;

        publisher.PublishRawAsync(Arg.Any<OutboxMessage>(), Arg.Any<OutboxMessageMetadata>(), Arg.Any<DispatchContext>())
            .Returns(ci =>
            {
                var msg = ci.Arg<OutboxMessage>();
                var current = Interlocked.Increment(ref callCount);
                if (current == 1)
                {
                    // First message experiences a transient broker glitch
                    return ValueTask.FromResult(DispatchResult.FailAndRetry(new TimeoutException("Broker busy")));
                }
                dispatchedList.Add(msg.Id);
                return ValueTask.FromResult(DispatchResult.Ok());
            });

        // Act: Start producer in background writing to bounded channel
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var producerTask = Task.Run(async () =>
        {
            foreach (var m in messages)
            {
                await channel.WriteAsync(m, cts.Token);
            }
            channel.Complete();
        }, cts.Token);

        await channel.ProcessMessagesAsync(cts.Token);
        await producerTask;

        // Assert: All 5 successful messages were dispatched and 1 was marked failed for retry
        dispatchedList.Should().HaveCount(5);
        await repo.Received().MarkAsFailedAsync(
            Arg.Is<IReadOnlyList<OutboxMessage>>(l => l.Count == 1 && l[0].Id == messages[0].Id),
            Arg.Is<string>(s => s.Contains("Broker busy")),
            false,
            Arg.Any<CancellationToken>());
    }
    }
}





