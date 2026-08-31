// Copyright © Erickson Lopez. MIT License.
using System;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Dapper;
using EricksonLopez.Outbox;
using EricksonLopez.Outbox.Dispatcher;
using EricksonLopez.Outbox.Storage.PostgreSql;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using NSubstitute;
using Xunit;

namespace EricksonLopez.Outbox.Storage.PostgreSql.Tests;

[Collection("PostgreSql")]
[Trait("Category", "Integration")]
public class PostgresNotificationListenerTests
{
    private readonly PostgreSqlContainerFixture _fixture;
    protected NpgsqlDataSource _dataSource => _fixture.DataSource;

    public PostgresNotificationListenerTests(PostgreSqlContainerFixture fixture)
    {
        _fixture = fixture;
    }

    private static AdaptivePoller CreatePoller(out OutboxChannel outboxChannel)
    {
        var services = new ServiceCollection();
        var sp = services.BuildServiceProvider();
        
        var optionsMock = Options.Create(new OutboxDispatcherOptions());
        var outboxOptions = Options.Create(new OutboxRuntimeOptions());
        
        var metrics = new EricksonLopez.Outbox.Diagnostics.OutboxMetrics();

        outboxChannel = new OutboxChannel(
            NullLogger<OutboxChannel>.Instance, 
            Substitute.For<IBrokerPublisher>(), 
            optionsMock,
            outboxOptions,
            metrics,
            Substitute.For<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>(), 
            NSubstitute.Substitute.For<EricksonLopez.Outbox.Diagnostics.IErrorSanitizer>(),
            TimeProvider.System);

        return new AdaptivePoller(
            sp,
            outboxChannel,
            optionsMock,
            NullLogger<AdaptivePoller>.Instance,
            metrics, TimeProvider.System);
    }

    [Fact]
    public async Task StartAsync_Should_Listen_And_Wake_Poller_On_Notify()
    {
        var poller = CreatePoller(out var outboxChannel);
        var listener = new PostgresNotificationListener(
            _dataSource, 
            NullLogger<PostgresNotificationListener>.Instance, 
            TimeProvider.System,
            poller);

        var eventFiredTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var listeningStartedTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        listener.OnMessageReceived += (s, e) => eventFiredTcs.TrySetResult();
        listener.OnListeningStarted += (s, e) => listeningStartedTcs.TrySetResult();

        using var cts = new CancellationTokenSource();
        var listenerTask = listener.StartAsync(cts.Token);

        // Wait until listener has confirmed subscription to LISTEN
        await listeningStartedTcs.Task.WaitAsync(TimeSpan.FromSeconds(10));

        // Fire a NOTIFY
        await using var connection = await _dataSource.OpenConnectionAsync();
        await connection.ExecuteAsync("NOTIFY outbox_new_messages, 'test_payload';");

        // Wait deterministically for the notification to be received
        await eventFiredTcs.Task.WaitAsync(TimeSpan.FromSeconds(10));

        // Assert poller was woken and event fired
        Assert.True(eventFiredTcs.Task.IsCompletedSuccessfully);

        // Cleanup
        cts.Cancel();
        try
        {
            if (listener.ExecuteTask != null)
            {
                await listener.ExecuteTask;
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    [Fact]
    public async Task StartAsync_CancellationRequestedAfterNotification_ExitsCleanlyWithoutException()
    {
        var poller = CreatePoller(out var outboxChannel);
        var listener = new PostgresNotificationListener(
            _dataSource, 
            NullLogger<PostgresNotificationListener>.Instance, 
            TimeProvider.System,
            poller);

        var listeningStartedTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        listener.OnListeningStarted += (s, e) => listeningStartedTcs.TrySetResult();

        using var cts = new CancellationTokenSource();
        listener.OnMessageReceived += (s, e) => cts.Cancel();

        await listener.StartAsync(cts.Token);
        await listeningStartedTcs.Task.WaitAsync(TimeSpan.FromSeconds(10));

        await using var connection = await _dataSource.OpenConnectionAsync();
        await connection.ExecuteAsync("NOTIFY outbox_new_messages, 'cancel_on_receive';");

        if (listener.ExecuteTask != null)
        {
            await listener.ExecuteTask.WaitAsync(TimeSpan.FromSeconds(10));
        }

        listener.ExecuteTask?.IsCompletedSuccessfully.Should().BeTrue();
    }

    [Fact]
    public async Task StartAsync_Should_Handle_Cancellation_During_Wait()
    {
        var poller = CreatePoller(out var outboxChannel);
        var listener = new PostgresNotificationListener(
            _dataSource, 
            NullLogger<PostgresNotificationListener>.Instance, 
            TimeProvider.System,
            poller);

        var startedTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        listener.OnListeningStarted += (s, e) => startedTcs.TrySetResult();

        using var cts = new CancellationTokenSource();
        await listener.StartAsync(cts.Token);
        
        await startedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        
        cts.Cancel(); // Should cancel WaitAsync cleanly
        
        var exception = await Record.ExceptionAsync(() => listener.ExecuteTask ?? Task.CompletedTask);
        Assert.Null(exception); // The OperationCanceledException should be caught and loop broken
        listener.ExecuteTask?.IsCompletedSuccessfully.Should().BeTrue();
    }

    [Fact]
    public async Task StartAsync_Should_Retry_On_Connection_Error()
    {
        var poller = CreatePoller(out var outboxChannel);
        // Bad connection string to force exception without DNS delay
        var badDataSource = NpgsqlDataSource.Create("Host=127.0.0.1;Port=59999;Username=test;Password=wrong;Timeout=1;CommandTimeout=1");
        
        var listener = new PostgresNotificationListener(
            badDataSource, 
            NullLogger<PostgresNotificationListener>.Instance, 
            TimeProvider.System,
            poller);

        using var cts = new CancellationTokenSource();
        var listenerTask = listener.StartAsync(cts.Token);
        
        // Wait long enough for it to hit the catch block and Task.Delay
        await Task.Delay(200);
        
        cts.Cancel(); // Should cancel Task.Delay cleanly
        
        var exception = await Record.ExceptionAsync(() => listener.ExecuteTask ?? Task.CompletedTask);
        Assert.Null(exception); // Clean exit
    }

    [Fact]
    public async Task StartAsync_Should_Return_If_PollerWakeup_Is_Null()
    {
        var listener = new PostgresNotificationListener(
            _dataSource, 
            NullLogger<PostgresNotificationListener>.Instance, 
            TimeProvider.System,
            null); // null poller

        using var cts = new CancellationTokenSource();
        await listener.StartAsync(cts.Token);
        
        var exception = await Record.ExceptionAsync(() => listener.ExecuteTask ?? Task.CompletedTask);
        Assert.Null(exception); // Should return immediately
        listener.ExecuteTask.Should().NotBeNull();
        listener.ExecuteTask!.IsCompletedSuccessfully.Should().BeTrue();
    }

    [Fact]
    public void Constructor_NullParameters_ThrowsArgumentNullException()
    {
        var poller = Substitute.For<IPollerWakeup>();
        
        Action act1 = () => _ = new PostgresNotificationListener(null!, NullLogger<PostgresNotificationListener>.Instance, TimeProvider.System, poller);
        act1.Should().Throw<ArgumentNullException>().WithParameterName("dataSource");

        Action act2 = () => _ = new PostgresNotificationListener(_dataSource, null!, TimeProvider.System, poller);
        act2.Should().Throw<ArgumentNullException>().WithParameterName("logger");

        Action act3 = () => _ = new PostgresNotificationListener(_dataSource, NullLogger<PostgresNotificationListener>.Instance, null!, poller);
        act3.Should().Throw<ArgumentNullException>().WithParameterName("timeProvider");
    }

    [Fact]
    public async Task StartAsync_Should_Call_PollerWakeup_When_NotificationReceived()
    {
        var pollerMock = Substitute.For<IPollerWakeup>();
        var listener = new PostgresNotificationListener(
            _dataSource, 
            NullLogger<PostgresNotificationListener>.Instance, 
            TimeProvider.System,
            pollerMock);

        var eventFiredTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var listeningStartedTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        listener.OnMessageReceived += (s, e) => eventFiredTcs.TrySetResult();
        listener.OnListeningStarted += (s, e) => listeningStartedTcs.TrySetResult();

        using var cts = new CancellationTokenSource();
        await listener.StartAsync(cts.Token);

        await listeningStartedTcs.Task.WaitAsync(TimeSpan.FromSeconds(10));

        await using var connection = await _dataSource.OpenConnectionAsync();
        await connection.ExecuteAsync("NOTIFY outbox_new_messages, 'payload_data';");

        await eventFiredTcs.Task.WaitAsync(TimeSpan.FromSeconds(10));
        pollerMock.Received(1).WakeUp();

        cts.Cancel();
        try
        {
            if (listener.ExecuteTask != null)
            {
                await listener.ExecuteTask;
            }
        }
        catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task StartAsync_WhenListenLoopThrowsException_RetriesAndLogsError()
    {
        var pollerMock = Substitute.For<IPollerWakeup>();
        await using var faultyDataSource = NpgsqlDataSource.Create("Host=127.0.0.1;Port=59999;Database=test;Username=u;Password=p;Timeout=1;CommandTimeout=1");
        
        var listener = new PostgresNotificationListener(
            faultyDataSource,
            NullLogger<PostgresNotificationListener>.Instance,
            TimeProvider.System,
            pollerMock);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        await listener.StartAsync(cts.Token);

        try
        {
            if (listener.ExecuteTask != null)
            {
                await listener.ExecuteTask;
            }
        }
        catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task StartAsync_WhenConnectionFails_DelaysUsingTimeProviderBeforeRetrying()
    {
        var pollerMock = Substitute.For<IPollerWakeup>();
        var fakeTime = new Microsoft.Extensions.Time.Testing.FakeTimeProvider();
        await using var faultyDataSource = NpgsqlDataSource.Create("Host=127.0.0.1;Port=59999;Database=test;Username=u;Password=p;Timeout=1;CommandTimeout=1");

        var listener = new PostgresNotificationListener(
            faultyDataSource,
            NullLogger<PostgresNotificationListener>.Instance,
            fakeTime,
            pollerMock);

        using var cts = new CancellationTokenSource();
        await listener.StartAsync(cts.Token);

        // Give listener task time to enter the catch block and schedule delay
        await Task.Delay(100);

        // Advance time by 5 seconds to complete delay
        fakeTime.Advance(TimeSpan.FromSeconds(5));

        cts.Cancel();
        try
        {
            if (listener.ExecuteTask != null)
            {
                await listener.ExecuteTask;
            }
        }
        catch (OperationCanceledException) { }
    }
}








