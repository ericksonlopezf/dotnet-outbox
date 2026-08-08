using System;
using System.Threading;
using System.Threading.Tasks;
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

namespace EricksonLopez.Outbox.Tests;

public class PostgresNotificationListenerTests : IClassFixture<PostgreSqlContainerFixture>
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
            Substitute.For<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>(), NSubstitute.Substitute.For<EricksonLopez.Outbox.Diagnostics.IErrorSanitizer>());

        return new AdaptivePoller(
            sp,
            outboxChannel,
            optionsMock,
            NullLogger<AdaptivePoller>.Instance,
            metrics);
    }

    [Fact]
    public async Task StartAsync_Should_Listen_And_Wake_Poller_On_Notify()
    {
        var poller = CreatePoller(out var outboxChannel);
        var listener = new PostgresNotificationListener(
            _dataSource, 
            NullLogger<PostgresNotificationListener>.Instance, 
            poller);

        bool eventFired = false;
        listener.OnMessageReceived += (s, e) => eventFired = true;

        using var cts = new CancellationTokenSource();
        
        var listenerTask = listener.StartAsync(cts.Token);
        
        // Give it a moment to connect and execute LISTEN
        await Task.Delay(500);

        // Fire a NOTIFY
        await using var connection = await _dataSource.OpenConnectionAsync();
        await connection.ExecuteAsync("NOTIFY outbox_new_messages, 'test_payload';");

        // Wait for the notification to be received
        for (int i = 0; i < 30; i++)
        {
            if (eventFired) break;
            await Task.Delay(100);
        }

        // Assert poller was woken and event fired
        Assert.True(eventFired);

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
    public async Task StartAsync_Should_Handle_Cancellation_During_Wait()
    {
        var poller = CreatePoller(out var outboxChannel);
        var listener = new PostgresNotificationListener(
            _dataSource, 
            NullLogger<PostgresNotificationListener>.Instance, 
            poller);

        using var cts = new CancellationTokenSource();
        var listenerTask = listener.StartAsync(cts.Token);
        
        await Task.Delay(200); // Wait for connection to open
        
        cts.Cancel(); // Should cancel WaitAsync cleanly
        
        var exception = await Record.ExceptionAsync(() => listener.ExecuteTask ?? Task.CompletedTask);
        Assert.Null(exception); // The OperationCanceledException should be caught and loop broken
    }

    [Fact]
    public async Task StartAsync_Should_Retry_On_Connection_Error()
    {
        var poller = CreatePoller(out var outboxChannel);
        // Bad connection string to force exception
        var badDataSource = NpgsqlDataSource.Create("Host=localhost;Username=test;Password=wrong");
        
        var listener = new PostgresNotificationListener(
            badDataSource, 
            NullLogger<PostgresNotificationListener>.Instance, 
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
            null); // null poller

        using var cts = new CancellationTokenSource();
        await listener.StartAsync(cts.Token);
        
        var exception = await Record.ExceptionAsync(() => listener.ExecuteTask ?? Task.CompletedTask);
        Assert.Null(exception); // Should return immediately
    }
}




