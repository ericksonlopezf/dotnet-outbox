using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using Npgsql;
using EricksonLopez.Outbox.Dispatcher;
namespace EricksonLopez.Outbox.Storage.PostgreSql;

/// <summary>
/// A dedicated background listener for PostgreSQL <c>LISTEN</c>/<c>NOTIFY</c> commands.
/// </summary>
/// <remarks>
/// <para>
/// Provides sub-millisecond wakeup latency for the Outbox Dispatcher by actively listening
/// to PostgreSQL notifications rather than relying solely on polling.
/// </para>
/// <para>
/// Includes an infinite retry loop to handle database disconnects gracefully.
/// </para>
/// </remarks>

public sealed class PostgresNotificationListener : BackgroundService
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly ILogger<PostgresNotificationListener> _logger;
    private readonly IPollerWakeup? _pollerWakeup;

    /// <summary>
    /// Initializes a new instance of the <see cref="PostgresNotificationListener"/> class.
    /// </summary>
    /// <param name="dataSource">The PostgreSQL data source that creates connections.</param>
    /// <param name="logger">The logger that records listener activity and errors.</param>
    /// <param name="pollerWakeup">The wakeup signal interface to trigger the dispatcher poller (optional).</param>
    [CLSCompliant(false)]
    public PostgresNotificationListener(NpgsqlDataSource dataSource, ILogger<PostgresNotificationListener> logger, IPollerWakeup? pollerWakeup = null)
    {
        _dataSource = dataSource;
        _logger = logger;
        _pollerWakeup = pollerWakeup;
    }

    /// <summary>
    /// Executes the continuous listening loop for PostgreSQL notifications.
    /// </summary>
    /// <param name="stoppingToken">A cancellation token that signals when the listener should terminate.</param>
    /// <returns>A task that represents the asynchronous execution of the listener.</returns>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_pollerWakeup == null)
        {
            _logger.LogInformation("No IPollerWakeup registered. PostgresNotificationListener will run in idle mode.");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ListenLoopAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in PostgreSQL notification listener. Retrying in 5 seconds...");
                
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    private async Task ListenLoopAsync(CancellationToken stoppingToken)
    {
        await using var connection = _dataSource.CreateConnection();
        
        connection.Notification += (o, e) =>
        {
            _logger.LogDebug("Received notification on {Channel}: {Payload}", e.Channel, e.Payload);
            _pollerWakeup?.WakeUp();
            OnMessageReceived?.Invoke(this, EventArgs.Empty);
        };

        await connection.OpenAsync(stoppingToken).ConfigureAwait(false);

        await using (var cmd = new NpgsqlCommand("LISTEN outbox_new_messages;", connection))
        {
            await cmd.ExecuteNonQueryAsync(stoppingToken).ConfigureAwait(false);
        }

        _logger.LogInformation("Listening for PostgreSQL notifications on 'outbox_new_messages'...");

        while (connection.State == ConnectionState.Open && !stoppingToken.IsCancellationRequested)
        {
            await connection.WaitAsync(stoppingToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Event raised when a notification message is received from PostgreSQL.
    /// </summary>
    public event EventHandler? OnMessageReceived;
}
