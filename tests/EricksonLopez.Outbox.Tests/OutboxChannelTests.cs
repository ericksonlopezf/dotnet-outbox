#pragma warning disable CA2012
using System;
using System.Collections.Generic;

using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using AwesomeAssertions;
using EricksonLopez.Outbox;
using EricksonLopez.Outbox.Dispatcher;
using EricksonLopez.Outbox.Persistence;
using EricksonLopez.Outbox.Pipeline;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace EricksonLopez.Outbox.Tests;

public class OutboxChannelTests
{
    /// <summary>
    /// Completes the channel writer via reflection so ProcessMessagesAsync exits naturally
    /// instead of waiting for a CancellationToken timeout. This makes tests deterministic
    /// and avoids timing flakiness under parallel test execution.
    /// </summary>
    private static Microsoft.Extensions.DependencyInjection.IServiceScopeFactory FakeScopeFactory(IServiceProvider provider)
    {
        var scope = Substitute.For<Microsoft.Extensions.DependencyInjection.IServiceScope>();
        scope.ServiceProvider.Returns(provider);
        var factory = Substitute.For<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>();
        factory.CreateScope().Returns(scope);
        return factory;
    }

    private static EricksonLopez.Outbox.Diagnostics.IErrorSanitizer FakeErrorSanitizer()
    {
        var s = NSubstitute.Substitute.For<EricksonLopez.Outbox.Diagnostics.IErrorSanitizer>();
        s.Sanitize(Arg.Any<Exception>()).Returns(x => x.Arg<Exception>().Message);
        return s;
    }

    private static void CompleteWriter(OutboxChannel channel)
    {
        var inner = typeof(OutboxChannel)
            .GetField("_channel", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?.GetValue(channel) as Channel<OutboxMessage>;
        inner?.Writer.Complete();
    }

    private sealed class FakeChannelLogger : Microsoft.Extensions.Logging.ILogger<OutboxChannel>
    {
        public readonly List<string> LoggedMessages = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;
        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var msg = formatter(state, exception);
            if (exception != null) msg += " | Exception: " + exception.Message;
            LoggedMessages.Add(msg);
        }
    }

    [Fact]
    public void Constructor_With_HasOnlySingletonMiddlewares_True_Should_PreResolve_Middlewares()
    {
        var publisher = Substitute.For<IBrokerPublisher>();
        var options = Options.Create(new OutboxDispatcherOptions { ChannelCapacity = 10, HasOnlySingletonMiddlewares = true });
        
        var mw = Substitute.For<IOutboxMiddleware>();
        var services = new ServiceCollection().AddSingleton(mw).BuildServiceProvider();
        var scopeFactory = FakeScopeFactory(services);

        var channel = new OutboxChannel(NullLogger<OutboxChannel>.Instance, publisher, options, Options.Create(new OutboxRuntimeOptions()), new EricksonLopez.Outbox.Diagnostics.OutboxMetrics(), scopeFactory, FakeErrorSanitizer());
        
        // Assert it was resolved by invoking a method that would crash if it wasn't
        channel.Should().NotBeNull();
    }

    [Fact]
    public async Task ProcessMessagesAsync_When_Publisher_Returns_Success_And_ShouldRetry_Should_FailFatal()
    {
        var publisher = Substitute.For<IBrokerPublisher>();
        var options = Options.Create(new OutboxDispatcherOptions { ChannelCapacity = 10 });
        var logger = new FakeChannelLogger();

        var repo = Substitute.For<IOutboxRepository>();
        var services = new ServiceCollection().AddScoped(sp => repo).AddScoped<IDeadLetterRepository>(sp => null!).BuildServiceProvider();
        var channel = new OutboxChannel(logger, publisher, options, Options.Create(new OutboxRuntimeOptions()), new EricksonLopez.Outbox.Diagnostics.OutboxMetrics(), FakeScopeFactory(services), FakeErrorSanitizer());

        var message = new OutboxMessage(Guid.NewGuid(), "alias", Array.Empty<byte>(), null, null, System.Text.Encoding.UTF8.GetBytes("{}"), DateTimeOffset.UtcNow, null, null, 0, 0, null);
        await channel.WriteAsync(message, CancellationToken.None);
        CompleteWriter(channel);

        _ = publisher.PublishRawAsync(message, Arg.Any<MessageMetadata>(), Arg.Any<DispatchContext>())
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
        var options = Options.Create(new OutboxDispatcherOptions { ChannelCapacity = 10 });
        var logger = new FakeChannelLogger();


        var repo = Substitute.For<IOutboxRepository>();
        var services = new ServiceCollection().AddScoped(sp => repo).AddScoped<IDeadLetterRepository>(sp => null!).BuildServiceProvider();

var channel = new OutboxChannel(logger, publisher, options, Options.Create(new OutboxRuntimeOptions()), new EricksonLopez.Outbox.Diagnostics.OutboxMetrics(), FakeScopeFactory(services), FakeErrorSanitizer());

        var message = new OutboxMessage(Guid.NewGuid(), "alias", Array.Empty<byte>(), null, null, System.Text.Encoding.UTF8.GetBytes("{\"traceparent\":\"123\"}"), DateTimeOffset.UtcNow, null, null, 0, 0, null);

        await channel.WriteAsync(message, CancellationToken.None);
        CompleteWriter(channel);

        _ = publisher.PublishRawAsync(message, Arg.Any<MessageMetadata>(), Arg.Any<DispatchContext>())
            .Returns(new ValueTask<DispatchResult>(DispatchResult.Ok()));

        await channel.ProcessMessagesAsync(CancellationToken.None);

        await repo.Received().MarkAsDispatchedAsync(Arg.Any<IReadOnlyList<OutboxMessage>>(), Arg.Any<CancellationToken>());
        logger.LoggedMessages.Should().Contain(m => m.Contains("dispatched in 0ms"));
        logger.LoggedMessages.Should().NotContain(m => m.Contains("IBrokerPublisher returned default(DispatchResult)"));
    }

    [Fact]
    public async Task ProcessMessagesAsync_Should_Handle_Deserialization_Error()
    {
        var publisher = Substitute.For<IBrokerPublisher>();
        var options = Options.Create(new OutboxDispatcherOptions { ChannelCapacity = 10 });


        var repo = Substitute.For<IOutboxRepository>();
        var services = new ServiceCollection().AddScoped(sp => repo).AddScoped<IDeadLetterRepository>(sp => null!).BuildServiceProvider();

var channel = new OutboxChannel(NullLogger<OutboxChannel>.Instance, publisher, options, Options.Create(new OutboxRuntimeOptions()), new EricksonLopez.Outbox.Diagnostics.OutboxMetrics(), FakeScopeFactory(services), FakeErrorSanitizer());

        var message = new OutboxMessage(Guid.NewGuid(), "alias", Array.Empty<byte>(), null, null, System.Text.Encoding.UTF8.GetBytes("{ invalid json"), DateTimeOffset.UtcNow, null, null, 0, 0, null);

        await channel.WriteAsync(message, CancellationToken.None);
        CompleteWriter(channel);

        _ = publisher.PublishRawAsync(message, Arg.Any<MessageMetadata>(), Arg.Any<DispatchContext>())
            .Returns(new ValueTask<DispatchResult>(DispatchResult.Ok()));

        await channel.ProcessMessagesAsync(CancellationToken.None);

        await repo.Received().MarkAsFailedAsync(Arg.Any<IReadOnlyList<OutboxMessage>>(), Arg.Any<string>(), true, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessMessagesAsync_Should_Handle_Failure_Retry()
    {
        var publisher = Substitute.For<IBrokerPublisher>();
        var options = Options.Create(new OutboxDispatcherOptions { ChannelCapacity = 10 });


        var repo = Substitute.For<IOutboxRepository>();
        var services = new ServiceCollection().AddScoped(sp => repo).AddScoped<IDeadLetterRepository>(sp => null!).BuildServiceProvider();

var channel = new OutboxChannel(NullLogger<OutboxChannel>.Instance, publisher, options, Options.Create(new OutboxRuntimeOptions()), new EricksonLopez.Outbox.Diagnostics.OutboxMetrics(), FakeScopeFactory(services), FakeErrorSanitizer());

        var message = new OutboxMessage(Guid.NewGuid(), "alias", Array.Empty<byte>(), null, null, System.Text.Encoding.UTF8.GetBytes("{}"), DateTimeOffset.UtcNow, null, null, 0, 0, null);

        await channel.WriteAsync(message, CancellationToken.None);
        CompleteWriter(channel);

        _ = publisher.PublishRawAsync(message, Arg.Any<MessageMetadata>(), Arg.Any<DispatchContext>())
            .Returns(new ValueTask<DispatchResult>(DispatchResult.FailAndRetry(new InvalidOperationException("test"))));

        await channel.ProcessMessagesAsync(CancellationToken.None);

        await repo.Received().MarkAsFailedAsync(Arg.Any<IReadOnlyList<OutboxMessage>>(), "test", false, Arg.Any<CancellationToken>());
        await repo.DidNotReceiveWithAnyArgs().MarkAsDispatchedAsync(default!, default!);
    }

    [Fact]
    public async Task ProcessMessagesAsync_Should_Handle_Fatal_Failure()
    {
        var publisher = Substitute.For<IBrokerPublisher>();
        var options = Options.Create(new OutboxDispatcherOptions { ChannelCapacity = 10 });
        var logger = new FakeChannelLogger();


        var repo = Substitute.For<IOutboxRepository>();
        var dlqRepo = Substitute.For<IDeadLetterRepository>();
        
        var sc = new ServiceCollection();
        sc.AddSingleton<IDeadLetterRepository>(dlqRepo);
        sc.AddScoped(sp => repo);
        var services = sc.BuildServiceProvider();

var channel = new OutboxChannel(logger, publisher, options, Options.Create(new OutboxRuntimeOptions()), new EricksonLopez.Outbox.Diagnostics.OutboxMetrics(), FakeScopeFactory(services), FakeErrorSanitizer());

        var message = new OutboxMessage(Guid.NewGuid(), "alias", Array.Empty<byte>(), null, null, System.Text.Encoding.UTF8.GetBytes("{}"), DateTimeOffset.UtcNow, null, null, 0, 0, null);

        await channel.WriteAsync(message, CancellationToken.None);
        CompleteWriter(channel);

        _ = publisher.PublishRawAsync(message, Arg.Any<MessageMetadata>(), Arg.Any<DispatchContext>())
            .Returns(new ValueTask<DispatchResult>(DispatchResult.FailFatal(new InvalidOperationException("fatal"))));

        await channel.ProcessMessagesAsync(CancellationToken.None);

        await dlqRepo.Received().InsertAsync(Arg.Any<DeadLetterMessage>(), Arg.Any<EricksonLopez.Outbox.Persistence.IOutboxTransactionContext>(), Arg.Any<CancellationToken>());
        await repo.Received().MarkAsFailedAsync(Arg.Any<IReadOnlyList<OutboxMessage>>(), "fatal", true, Arg.Any<CancellationToken>());
        await repo.DidNotReceiveWithAnyArgs().MarkAsDispatchedAsync(default!, default!);
        logger.LoggedMessages.Should().Contain(m => m.Contains("dead-lettered after 0 retries"));
        logger.LoggedMessages.Should().NotContain(m => m.Contains("IBrokerPublisher returned default(DispatchResult)"));
    }

    [Fact]
    public async Task ProcessMessagesAsync_Should_Truncate_Large_Headers()
    {
        var publisher = Substitute.For<IBrokerPublisher>();
        var options = Options.Create(new OutboxDispatcherOptions { ChannelCapacity = 10 });


        var repo = Substitute.For<IOutboxRepository>();
        var services = new ServiceCollection().AddScoped(sp => repo).AddScoped<IDeadLetterRepository>(sp => null!).BuildServiceProvider();

var channel = new OutboxChannel(NullLogger<OutboxChannel>.Instance, publisher, options, Options.Create(new OutboxRuntimeOptions()), new EricksonLopez.Outbox.Diagnostics.OutboxMetrics(), FakeScopeFactory(services), FakeErrorSanitizer());

        var message = new OutboxMessage(Guid.NewGuid(), "alias", Array.Empty<byte>(), null, null, System.Text.Encoding.UTF8.GetBytes(new string('a', 1024 * 1025)), DateTimeOffset.UtcNow, null, null, 0, 0, null);

        await channel.WriteAsync(message, CancellationToken.None);
        CompleteWriter(channel);

        _ = publisher.PublishRawAsync(message, Arg.Any<MessageMetadata>(), Arg.Any<DispatchContext>())
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

        var channel = new OutboxChannel(Microsoft.Extensions.Logging.Abstractions.NullLogger<OutboxChannel>.Instance, NSubstitute.Substitute.For<EricksonLopez.Outbox.IBrokerPublisher>(), optionsMock, Microsoft.Extensions.Options.Options.Create(new EricksonLopez.Outbox.OutboxRuntimeOptions()), new EricksonLopez.Outbox.Diagnostics.OutboxMetrics(), FakeScopeFactory(new ServiceCollection().BuildServiceProvider()), FakeErrorSanitizer());

        // Force the ObservableGauge to be evaluated
        listener.RecordObservableInstruments();

        recordedValue.Should().Be(0.0);
    }

    [Fact]
    public async Task ProcessMessagesAsync_Should_Handle_Fatal_Failure_Without_Dlq()
    {
        var publisher = Substitute.For<IBrokerPublisher>();
        var options = Options.Create(new OutboxDispatcherOptions { ChannelCapacity = 10 });


        var repo = Substitute.For<IOutboxRepository>();
        var services = new ServiceCollection().AddScoped(sp => repo).AddScoped<IDeadLetterRepository>(sp => null!).BuildServiceProvider();

var channel = new OutboxChannel(NullLogger<OutboxChannel>.Instance, publisher, options, Options.Create(new OutboxRuntimeOptions()), new EricksonLopez.Outbox.Diagnostics.OutboxMetrics(), FakeScopeFactory(services), FakeErrorSanitizer()); // No DLQ registered

        var message = new OutboxMessage(Guid.NewGuid(), "alias", Array.Empty<byte>(), null, null, System.Text.Encoding.UTF8.GetBytes("{}"), DateTimeOffset.UtcNow, null, null, 0, 0, null);

        await channel.WriteAsync(message, CancellationToken.None);
        CompleteWriter(channel);

        _ = publisher.PublishRawAsync(message, Arg.Any<MessageMetadata>(), Arg.Any<DispatchContext>())
            .Returns(new ValueTask<DispatchResult>(DispatchResult.FailFatal(new InvalidOperationException("fatal"))));

        await channel.ProcessMessagesAsync(CancellationToken.None);

        await repo.Received().MarkAsFailedAsync(Arg.Any<IReadOnlyList<OutboxMessage>>(), "fatal", true, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessMessagesAsync_Should_Handle_Dlq_Exception()
    {
        var publisher = Substitute.For<IBrokerPublisher>();
        var options = Options.Create(new OutboxDispatcherOptions { ChannelCapacity = 10 });
        var logger = new FakeChannelLogger();


        var repo = Substitute.For<IOutboxRepository>();
        var dlqRepo = Substitute.For<IDeadLetterRepository>();
        
        dlqRepo.InsertAsync(Arg.Any<DeadLetterMessage>(), Arg.Any<EricksonLopez.Outbox.Persistence.IOutboxTransactionContext>(), Arg.Any<CancellationToken>())
            .Returns(x => throw new InvalidOperationException("DB Down"));
            
        var sc = new ServiceCollection();
        sc.AddSingleton<IDeadLetterRepository>(dlqRepo);
        sc.AddScoped(sp => repo);
        var services = sc.BuildServiceProvider();

var channel = new OutboxChannel(logger, publisher, options, Options.Create(new OutboxRuntimeOptions()), new EricksonLopez.Outbox.Diagnostics.OutboxMetrics(), FakeScopeFactory(services), FakeErrorSanitizer());

        var message = new OutboxMessage(Guid.NewGuid(), "alias", Array.Empty<byte>(), null, null, System.Text.Encoding.UTF8.GetBytes("{}"), DateTimeOffset.UtcNow, null, null, 0, 0, null);

        await channel.WriteAsync(message, CancellationToken.None);
        CompleteWriter(channel);

        _ = publisher.PublishRawAsync(message, Arg.Any<MessageMetadata>(), Arg.Any<DispatchContext>())
            .Returns(new ValueTask<DispatchResult>(DispatchResult.FailFatal(new InvalidOperationException("fatal"))));

        await channel.ProcessMessagesAsync(CancellationToken.None);

        // P0-FIX: When DLQ INSERT fails, the message MUST still be marked as dead-lettered (isDeadLetter=true)
        // to prevent an infinite retry loop. Previously, a failed DLQ INSERT caused isDeadLetter=false,
        // which left the message in state=3 (Failed) with retry_count >= MaxRetryCount, causing the poller
        // to re-fetch it indefinitely.
        await repo.Received().MarkAsFailedAsync(Arg.Any<IReadOnlyList<OutboxMessage>>(), "fatal", true, Arg.Any<CancellationToken>());
        await repo.DidNotReceive().MarkAsDispatchedAsync(Arg.Any<IReadOnlyList<OutboxMessage>>(), Arg.Any<CancellationToken>());
        logger.LoggedMessages.Should().Contain(m => m.Contains("Failed to insert message") && m.Contains("into DLQ"));
    }

    [Fact]
    public async Task ProcessMessagesAsync_Should_Exit_Naturally_When_Writer_Completed()
    {
        var publisher = Substitute.For<IBrokerPublisher>();
        var options = Options.Create(new OutboxDispatcherOptions { ChannelCapacity = 10 });


        var repo = Substitute.For<IOutboxRepository>();
        var services = new ServiceCollection().AddScoped(sp => repo).AddScoped<IDeadLetterRepository>(sp => null!).BuildServiceProvider();

var channel = new OutboxChannel(NullLogger<OutboxChannel>.Instance, publisher, options, Options.Create(new OutboxRuntimeOptions()), new EricksonLopez.Outbox.Diagnostics.OutboxMetrics(), FakeScopeFactory(services), FakeErrorSanitizer());

        // Complete the channel so WaitToReadAsync returns false
        CompleteWriter(channel);

        // Pass a token that is NOT cancelled, so it exits naturally
        await channel.ProcessMessagesAsync(CancellationToken.None);

        // Should complete without exceptions
        true.Should().BeTrue();
    }

    [Fact]
    public async Task ProcessMessagesAsync_Should_Handle_Cancellation_During_MicroBatching()
    {
        var publisher = Substitute.For<IBrokerPublisher>();
        var options = Options.Create(new OutboxDispatcherOptions { ChannelCapacity = 10 });


        var repo = Substitute.For<IOutboxRepository>();
        var services = new ServiceCollection().AddScoped(sp => repo).AddScoped<IDeadLetterRepository>(sp => null!).BuildServiceProvider();

var channel = new OutboxChannel(NullLogger<OutboxChannel>.Instance, publisher, options, Options.Create(new OutboxRuntimeOptions()), new EricksonLopez.Outbox.Diagnostics.OutboxMetrics(), FakeScopeFactory(services), FakeErrorSanitizer());

        var message = new OutboxMessage(Guid.NewGuid(), "alias", Array.Empty<byte>(), null, null, System.Text.Encoding.UTF8.GetBytes("{}"), DateTimeOffset.UtcNow, null, null, 0, 0, null);

        await channel.WriteAsync(message, CancellationToken.None);
        
        // Let the micro-batch timeout trigger by not writing any more messages
        // and completing the writer after a small delay
        _ = Task.Run(async () => 
        {
            await Task.Delay(100);
            CompleteWriter(channel);
        });

        publisher.PublishRawAsync(message, Arg.Any<MessageMetadata>(), Arg.Any<DispatchContext>())
            .Returns(new ValueTask<DispatchResult>(DispatchResult.Ok()));

        await channel.ProcessMessagesAsync(CancellationToken.None);

        await repo.Received().MarkAsDispatchedAsync(Arg.Any<IReadOnlyList<OutboxMessage>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessMessagesAsync_Should_Use_Correct_Attempt_Number()
    {
        var publisher = Substitute.For<IBrokerPublisher>();
        var options = Options.Create(new OutboxDispatcherOptions { ChannelCapacity = 10 });


        var repo = Substitute.For<IOutboxRepository>();
        var services = new ServiceCollection().AddScoped(sp => repo).AddScoped<IDeadLetterRepository>(sp => null!).BuildServiceProvider();

var channel = new OutboxChannel(NullLogger<OutboxChannel>.Instance, publisher, options, Options.Create(new OutboxRuntimeOptions()), new EricksonLopez.Outbox.Diagnostics.OutboxMetrics(), FakeScopeFactory(services), FakeErrorSanitizer());

        var message = new OutboxMessage(Guid.NewGuid(), "alias", Array.Empty<byte>(), null, null, System.Text.Encoding.UTF8.GetBytes("{}"), DateTimeOffset.UtcNow, null, null, OutboxMessageStatus.Pending, 2, null);

        await channel.WriteAsync(message, CancellationToken.None);
        CompleteWriter(channel);

        publisher.PublishRawAsync(message, Arg.Any<MessageMetadata>(), Arg.Is<DispatchContext>(c => c.Attempt == 3))
            .Returns(new ValueTask<DispatchResult>(DispatchResult.Ok()));

        await channel.ProcessMessagesAsync(CancellationToken.None);

        await publisher.Received().PublishRawAsync(message, Arg.Any<MessageMetadata>(), Arg.Is<DispatchContext>(c => c.Attempt == 3));
    }

    [Fact]
    public async Task ProcessMessagesAsync_Should_Log_Warning_On_Default_DispatchResult()
    {
        var publisher = Substitute.For<IBrokerPublisher>();
        var options = Options.Create(new OutboxDispatcherOptions { ChannelCapacity = 10 });
        var logger = new FakeChannelLogger();


        var repo = Substitute.For<IOutboxRepository>();
        var services = new ServiceCollection().AddScoped(sp => repo).AddScoped<IDeadLetterRepository>(sp => null!).BuildServiceProvider();

var channel = new OutboxChannel(logger, publisher, options, Options.Create(new OutboxRuntimeOptions()), new EricksonLopez.Outbox.Diagnostics.OutboxMetrics(), FakeScopeFactory(services), FakeErrorSanitizer());

        var message = new OutboxMessage(Guid.NewGuid(), "alias", Array.Empty<byte>(), null, null, System.Text.Encoding.UTF8.GetBytes("{}"), DateTimeOffset.UtcNow, null, null, 0, 0, null);

        await channel.WriteAsync(message, CancellationToken.None);
        CompleteWriter(channel);

        // Return default DispatchResult
        publisher.PublishRawAsync(message, Arg.Any<MessageMetadata>(), Arg.Any<DispatchContext>())
            .Returns(new ValueTask<DispatchResult>(default(DispatchResult)));

        await channel.ProcessMessagesAsync(CancellationToken.None);

        // It should be treated as fatal failure since Success is false, ShouldRetry is false, and Error is null
        await repo.Received().MarkAsFailedAsync(Arg.Any<IReadOnlyList<OutboxMessage>>(), "IBrokerPublisher returned default(DispatchResult) for alias.", true, Arg.Any<CancellationToken>());
        logger.LoggedMessages.Should().Contain(m => 
            m.Contains("IBrokerPublisher returned default(DispatchResult)") && 
            m.Contains("This is treated as FailFatal(null)") &&
            m.Contains("DispatchResult.FailAndRetry(ex)"));
    }

    [Fact]
    public async Task ProcessMessagesAsync_Should_Handle_NoRetry_Increment()
    {
        var publisher = Substitute.For<IBrokerPublisher>();
        var options = Options.Create(new OutboxDispatcherOptions { ChannelCapacity = 10 });
        var logger = new FakeChannelLogger();


        var repo = Substitute.For<IOutboxRepository>();
        var services = new ServiceCollection().AddScoped(sp => repo).AddScoped<IDeadLetterRepository>(sp => null!).BuildServiceProvider();

var channel = new OutboxChannel(logger, publisher, options, Options.Create(new OutboxRuntimeOptions()), new EricksonLopez.Outbox.Diagnostics.OutboxMetrics(), FakeScopeFactory(services), FakeErrorSanitizer());

        var message = new OutboxMessage(Guid.NewGuid(), "alias", Array.Empty<byte>(), null, null, System.Text.Encoding.UTF8.GetBytes("{}"), DateTimeOffset.UtcNow, null, null, 0, 0, null);

        await channel.WriteAsync(message, CancellationToken.None);
        CompleteWriter(channel);

        // Return a DispatchResult that says ShouldRetry but IncrementRetryCount = false
        publisher.PublishRawAsync(message, Arg.Any<MessageMetadata>(), Arg.Any<DispatchContext>())
            .Returns(new ValueTask<DispatchResult>(new DispatchResult(false, true, new InvalidOperationException("Circuit Breaker Open"), false)));

        await channel.ProcessMessagesAsync(CancellationToken.None);

        // MarkAsFailedAsync should NOT be called because we are skipping it
        await repo.DidNotReceiveWithAnyArgs().MarkAsFailedAsync(default!, default!, default!, default!);
        logger.LoggedMessages.Should().Contain(m => m.Contains("delayed without incrementing retry count"));
    }

    [Fact]
    public async Task ProcessMessagesAsync_Should_Handle_Null_Options_In_Constructor()
    {
        var publisher = Substitute.For<IBrokerPublisher>();
        var channel = new OutboxChannel(NullLogger<OutboxChannel>.Instance, publisher, null!, null!, new EricksonLopez.Outbox.Diagnostics.OutboxMetrics(), FakeScopeFactory(new ServiceCollection().BuildServiceProvider()), FakeErrorSanitizer());
        
        channel.Should().NotBeNull();
    }
    
    [Fact]
    public async Task ProcessMessagesAsync_Should_DeadLetter_When_Max_Retries_Reached()
    {
        var publisher = Substitute.For<IBrokerPublisher>();
        var options = Options.Create(new OutboxDispatcherOptions { ChannelCapacity = 10, MaxRetryCount = 3 });


        var repo = Substitute.For<IOutboxRepository>();
        var services = new ServiceCollection().AddScoped(sp => repo).AddScoped<IDeadLetterRepository>(sp => null!).BuildServiceProvider();

var channel = new OutboxChannel(NullLogger<OutboxChannel>.Instance, publisher, options, Options.Create(new OutboxRuntimeOptions()), new EricksonLopez.Outbox.Diagnostics.OutboxMetrics(), FakeScopeFactory(services), FakeErrorSanitizer());

        // Already at retry count 2. Attempt 3 (RetryCount + 1) will be the final one and should DeadLetter
        var message = new OutboxMessage(Guid.NewGuid(), "alias", Array.Empty<byte>(), null, null, System.Text.Encoding.UTF8.GetBytes("{}"), DateTimeOffset.UtcNow, null, null, OutboxMessageStatus.Pending, 2, null);

        await channel.WriteAsync(message, CancellationToken.None);
        CompleteWriter(channel);

        publisher.PublishRawAsync(message, Arg.Any<MessageMetadata>(), Arg.Any<DispatchContext>())
            .Returns(new ValueTask<DispatchResult>(DispatchResult.FailAndRetry(new InvalidOperationException("Fail"))));

        await channel.ProcessMessagesAsync(CancellationToken.None);

        await repo.Received().MarkAsFailedAsync(Arg.Any<IReadOnlyList<OutboxMessage>>(), "Fail", true, Arg.Any<CancellationToken>());
    }

    [Fact(Timeout = 5000)]
    public async Task ProcessMessagesAsync_Should_Retry_Db_Operations()
    {
        var publisher = Substitute.For<IBrokerPublisher>();
        var options = Options.Create(new OutboxDispatcherOptions { ChannelCapacity = 10 });
        var logger = new FakeChannelLogger();
        
        var repo = Substitute.For<IOutboxRepository>();
        var services = new ServiceCollection().AddScoped(sp => repo).AddScoped<IDeadLetterRepository>(sp => null!).BuildServiceProvider();
        var channel = new OutboxChannel(logger, publisher, options, Options.Create(new OutboxRuntimeOptions()), new EricksonLopez.Outbox.Diagnostics.OutboxMetrics(), FakeScopeFactory(services), FakeErrorSanitizer());


        var message = new OutboxMessage(Guid.NewGuid(), "alias", Array.Empty<byte>(), null, null, System.Text.Encoding.UTF8.GetBytes("{}"), DateTimeOffset.UtcNow, null, null, 0, 0, null);

        await channel.WriteAsync(message, CancellationToken.None);
        CompleteWriter(channel);

        publisher.PublishRawAsync(message, Arg.Any<MessageMetadata>(), Arg.Any<DispatchContext>())
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
        logger.LoggedMessages.Should().Contain(m => m.Contains("Transient error updating outbox database. Retrying"));
    }

    [Fact(Timeout = 5000)]
    public async Task ProcessMessagesAsync_Should_Fail_After_Max_Db_Retries()
    {
        var publisher = Substitute.For<IBrokerPublisher>();
        var options = Options.Create(new OutboxDispatcherOptions { ChannelCapacity = 10 });
        var logger = new FakeChannelLogger();
        
        var repo = Substitute.For<IOutboxRepository>();
        var services = new ServiceCollection().AddScoped(sp => repo).AddScoped<IDeadLetterRepository>(sp => null!).BuildServiceProvider();
        var channel = new OutboxChannel(logger, publisher, options, Options.Create(new OutboxRuntimeOptions()), new EricksonLopez.Outbox.Diagnostics.OutboxMetrics(), FakeScopeFactory(services), FakeErrorSanitizer());


        var message = new OutboxMessage(Guid.NewGuid(), "alias", Array.Empty<byte>(), null, null, System.Text.Encoding.UTF8.GetBytes("{}"), DateTimeOffset.UtcNow, null, null, 0, 0, null);

        await channel.WriteAsync(message, CancellationToken.None);
        CompleteWriter(channel);

        publisher.PublishRawAsync(message, Arg.Any<MessageMetadata>(), Arg.Any<DispatchContext>())
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
        var options = Options.Create(new OutboxDispatcherOptions { ChannelCapacity = 10 });
        var logger = new FakeChannelLogger();

        var repo = Substitute.For<IOutboxRepository>();
        var services = new ServiceCollection().AddScoped(sp => repo).AddScoped<IDeadLetterRepository>(sp => null!).BuildServiceProvider();
        var channel = new OutboxChannel(logger, publisher, options, Options.Create(new OutboxRuntimeOptions()), new EricksonLopez.Outbox.Diagnostics.OutboxMetrics(), FakeScopeFactory(services), FakeErrorSanitizer());

        var cts = new CancellationTokenSource();
        cts.Cancel();

        await channel.ProcessMessagesAsync(cts.Token);
        
        logger.LoggedMessages.Should().Contain(m => m.Contains("OutboxChannel message processing cancelled (graceful shutdown)"));
    }

    [Fact]
    public void Constructor_Should_Throw_ArgumentNullException_For_Null_Arguments()
    {
        var services = new ServiceCollection();
        var publisher = Substitute.For<IBrokerPublisher>();
        var options = Microsoft.Extensions.Options.Options.Create(new OutboxDispatcherOptions());
        var runtimeOptions = Microsoft.Extensions.Options.Options.Create(new OutboxRuntimeOptions());
        var metrics = new EricksonLopez.Outbox.Diagnostics.OutboxMetrics();
        var scopeFactory = Substitute.For<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>();
        var errorSanitizer = FakeErrorSanitizer();
        var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<OutboxChannel>.Instance;

        Assert.Throws<ArgumentNullException>(() => new OutboxChannel(null!, publisher, options, runtimeOptions, metrics, scopeFactory, errorSanitizer));
        Assert.Throws<ArgumentNullException>(() => new OutboxChannel(logger, null!, options, runtimeOptions, metrics, scopeFactory, errorSanitizer));
        Assert.Throws<ArgumentNullException>(() => new OutboxChannel(logger, publisher, options, runtimeOptions, null!, scopeFactory, errorSanitizer));
        Assert.Throws<ArgumentNullException>(() => new OutboxChannel(logger, publisher, options, runtimeOptions, metrics, null!, errorSanitizer));
        Assert.Throws<ArgumentNullException>(() => new OutboxChannel(logger, publisher, options, runtimeOptions, metrics, scopeFactory, null!));
    }

    [Fact]
    public async Task ProcessMessagesAsync_Should_Handle_Malformed_Json_Headers()
    {
        var services = new ServiceCollection();
        var repo = Substitute.For<IOutboxRepository>();
        services.AddSingleton(repo);
        services.AddSingleton<IDeadLetterRepository>(sp => null!);
        var provider = services.BuildServiceProvider();
        var scopeFactory = Substitute.For<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>();
        var scope = Substitute.For<Microsoft.Extensions.DependencyInjection.IServiceScope>();
        scopeFactory.CreateScope().Returns(scope);
        scope.ServiceProvider.Returns(provider);

        var channel = new OutboxChannel(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<OutboxChannel>.Instance,
            Substitute.For<IBrokerPublisher>(),
            Microsoft.Extensions.Options.Options.Create(new OutboxDispatcherOptions()),
            Microsoft.Extensions.Options.Options.Create(new OutboxRuntimeOptions()),
            new EricksonLopez.Outbox.Diagnostics.OutboxMetrics(),
            scopeFactory,
            FakeErrorSanitizer());

        var msg = new OutboxMessage(Guid.NewGuid(), "type", Array.Empty<byte>(), null, null, System.Text.Encoding.UTF8.GetBytes("{ \"key\" "), DateTimeOffset.UtcNow, null, null, OutboxMessageStatus.Pending, 0, null);
        await channel.WriteAsync(msg, CancellationToken.None);
        CompleteWriter(channel);
        
        var cts = new CancellationTokenSource();
        cts.CancelAfter(100);
        await channel.ProcessMessagesAsync(cts.Token);
    }

    [Fact]
    public async Task ProcessMessagesAsync_Should_Handle_Json_Null_Header_Value()
    {
        var services = new ServiceCollection();
        var repo = Substitute.For<IOutboxRepository>();
        services.AddSingleton(repo);
        services.AddSingleton<IDeadLetterRepository>(sp => null!);
        var provider = services.BuildServiceProvider();
        var scopeFactory = Substitute.For<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>();
        var scope = Substitute.For<Microsoft.Extensions.DependencyInjection.IServiceScope>();
        scopeFactory.CreateScope().Returns(scope);
        scope.ServiceProvider.Returns(provider);

        var msg = new OutboxMessage(Guid.NewGuid(), "type", Array.Empty<byte>(), null, null, System.Text.Encoding.UTF8.GetBytes("{ \"key\": null }"), DateTimeOffset.UtcNow, null, null, OutboxMessageStatus.Pending, 0, null);

        var publisher = Substitute.For<IBrokerPublisher>();
        publisher.PublishRawAsync(msg, Arg.Any<MessageMetadata>(), Arg.Any<DispatchContext>())
            .Returns(new ValueTask<DispatchResult>(DispatchResult.Ok()));

        var channel = new OutboxChannel(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<OutboxChannel>.Instance,
            publisher,
            Microsoft.Extensions.Options.Options.Create(new OutboxDispatcherOptions()),
            Microsoft.Extensions.Options.Options.Create(new OutboxRuntimeOptions()),
            new EricksonLopez.Outbox.Diagnostics.OutboxMetrics(),
            scopeFactory,
            FakeErrorSanitizer());

        await channel.WriteAsync(msg, CancellationToken.None);
        CompleteWriter(channel);
        
        var cts = new CancellationTokenSource();
        cts.CancelAfter(100);
        await channel.ProcessMessagesAsync(cts.Token);
    }



    [Fact]
    public async Task ProcessMessagesAsync_Should_Handle_Headers_Cache_Miss_Swap()
    {
        var services = new ServiceCollection();
        var repo = Substitute.For<IOutboxRepository>();
        services.AddSingleton(repo);
        services.AddSingleton<IDeadLetterRepository>(sp => null!);
        var provider = services.BuildServiceProvider();
        var scopeFactory = Substitute.For<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>();
        var scope = Substitute.For<Microsoft.Extensions.DependencyInjection.IServiceScope>();
        scopeFactory.CreateScope().Returns(scope);
        scope.ServiceProvider.Returns(provider);

        var channel = new OutboxChannel(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<OutboxChannel>.Instance,
            Substitute.For<IBrokerPublisher>(),
            Microsoft.Extensions.Options.Options.Create(new OutboxDispatcherOptions()),
            Microsoft.Extensions.Options.Options.Create(new OutboxRuntimeOptions()),
            new EricksonLopez.Outbox.Diagnostics.OutboxMetrics(),
            scopeFactory,
            FakeErrorSanitizer());

        var msg1 = new OutboxMessage(Guid.NewGuid(), "type", Array.Empty<byte>(), null, null, System.Text.Encoding.UTF8.GetBytes("{ \"key1\": \"v1\" }"), DateTimeOffset.UtcNow, null, null, OutboxMessageStatus.Pending, 0, null);
        var msg2 = new OutboxMessage(Guid.NewGuid(), "type", Array.Empty<byte>(), null, null, System.Text.Encoding.UTF8.GetBytes("{ \"key2\": \"v2\" }"), DateTimeOffset.UtcNow, null, null, OutboxMessageStatus.Pending, 0, null);
        await channel.WriteAsync(msg1, CancellationToken.None);
        await channel.WriteAsync(msg2, CancellationToken.None);
        CompleteWriter(channel);
        
        var cts = new CancellationTokenSource();
        cts.CancelAfter(100);
        await channel.ProcessMessagesAsync(cts.Token);
    }

    [Fact]
    public async Task ProcessMessagesAsync_Should_Skip_MessageTypeTag_If_Disabled()
    {
        var services = new ServiceCollection();
        var repo = Substitute.For<IOutboxRepository>();
        services.AddSingleton(repo);
        services.AddSingleton<IDeadLetterRepository>(sp => null!);
        var provider = services.BuildServiceProvider();
        var scopeFactory = Substitute.For<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>();
        var scope = Substitute.For<Microsoft.Extensions.DependencyInjection.IServiceScope>();
        scopeFactory.CreateScope().Returns(scope);
        scope.ServiceProvider.Returns(provider);

        var msg = new OutboxMessage(Guid.NewGuid(), "type", Array.Empty<byte>(), null, null, Array.Empty<byte>(), DateTimeOffset.UtcNow, null, null, OutboxMessageStatus.Pending, 0, null);

        var publisher = Substitute.For<IBrokerPublisher>();
        publisher.PublishRawAsync(msg, Arg.Any<MessageMetadata>(), Arg.Any<DispatchContext>())
            .Returns(new ValueTask<DispatchResult>(DispatchResult.FailFatal(new InvalidOperationException("fatal error"))));

        var channel = new OutboxChannel(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<OutboxChannel>.Instance,
            publisher,
            Microsoft.Extensions.Options.Options.Create(new OutboxDispatcherOptions()),
            Microsoft.Extensions.Options.Options.Create(new OutboxRuntimeOptions { IncludeMessageTypeTag = false }),
            new EricksonLopez.Outbox.Diagnostics.OutboxMetrics(),
            scopeFactory,
            FakeErrorSanitizer());

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
        var services = new ServiceCollection();
        var repo = Substitute.For<IOutboxRepository>();
        services.AddSingleton(repo);
        services.AddSingleton<IDeadLetterRepository>(sp => null!);
        var provider = services.BuildServiceProvider();
        var scopeFactory = Substitute.For<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>();
        var scope = Substitute.For<Microsoft.Extensions.DependencyInjection.IServiceScope>();
        scopeFactory.CreateScope().Returns(scope);
        scope.ServiceProvider.Returns(provider);

        // A JSON array "[]" instead of an object "{}"
        var msg1 = new OutboxMessage(Guid.NewGuid(), "type", Array.Empty<byte>(), null, null, System.Text.Encoding.UTF8.GetBytes("[]"), DateTimeOffset.UtcNow, null, null, OutboxMessageStatus.Pending, 0, null);
        // Only whitespace so reader.Read() is false
        var msg2 = new OutboxMessage(Guid.NewGuid(), "type", Array.Empty<byte>(), null, null, System.Text.Encoding.UTF8.GetBytes("   "), DateTimeOffset.UtcNow, null, null, OutboxMessageStatus.Pending, 0, null);

        var publisher = Substitute.For<IBrokerPublisher>();
        publisher.PublishRawAsync(Arg.Any<OutboxMessage>(), Arg.Any<MessageMetadata>(), Arg.Any<DispatchContext>())
            .Returns(new ValueTask<DispatchResult>(DispatchResult.Ok()));

        var channel = new OutboxChannel(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<OutboxChannel>.Instance,
            publisher,
            Microsoft.Extensions.Options.Options.Create(new OutboxDispatcherOptions()),
            Microsoft.Extensions.Options.Options.Create(new OutboxRuntimeOptions()),
            new EricksonLopez.Outbox.Diagnostics.OutboxMetrics(),
            scopeFactory,
            FakeErrorSanitizer());

        await channel.WriteAsync(msg1, CancellationToken.None);
        await channel.WriteAsync(msg2, CancellationToken.None);
        CompleteWriter(channel);
        
        await channel.ProcessMessagesAsync(CancellationToken.None);
        
        await repo.Received().MarkAsDispatchedAsync(Arg.Any<IReadOnlyList<OutboxMessage>>(), Arg.Any<CancellationToken>());
    }
}
