// Copyright © Erickson Lopez. MIT License.
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace EricksonLopez.Outbox.Storage.PostgreSql;

/// <summary>
/// Validates that the connected PostgreSQL server is version 15 or higher.
/// </summary>
/// <remarks>
/// This validation ensures the correct concurrency semantics of <c>FOR UPDATE SKIP LOCKED</c>
/// in combination with Common Table Expressions (CTEs) and partitioned tables.
/// </remarks>
internal sealed class PostgreSqlVersionValidator : IHostedService
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly ILogger<PostgreSqlVersionValidator> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="PostgreSqlVersionValidator"/> class.
    /// </summary>
    /// <param name="dataSource">The PostgreSQL data source to connect to.</param>
    /// <param name="logger">The logger that records validation warnings or errors.</param>
    public PostgreSqlVersionValidator(NpgsqlDataSource dataSource, ILogger<PostgreSqlVersionValidator> logger)
    {
        _dataSource = dataSource;
        _logger = logger;
    }

    /// <summary>
    /// Connects to the database to retrieve and validate the server version.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token that signals when the operation should be aborted.</param>
    /// <returns>A task that represents the asynchronous startup validation.</returns>
    /// <exception cref="NotSupportedException">Thrown if the PostgreSQL server version is strictly less than 15.</exception>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var conn = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var cmd = new NpgsqlCommand("SHOW server_version_num;", conn);
            
            var result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            if (result != null && int.TryParse(result.ToString(), out var versionNum))
            {
                ValidateServerVersion(versionNum, _logger);
            }
        }
        catch (Exception ex) when (ex is not NotSupportedException)
        {
            // Do not fail startup if the DB is temporarily down, the poller will retry.
            // But log a warning that version validation could not be completed.
            _logger.LogWarning(ex, "Could not validate PostgreSQL server version during startup.");
        }
    }

    internal static void ValidateServerVersion(int versionNum, ILogger logger)
    {
        if (versionNum < 150000)
        {
            logger.LogCritical("EricksonLopez.Outbox requires PostgreSQL 15 or higher. Detected version: {VersionNum}", versionNum);
            throw new NotSupportedException($"EricksonLopez.Outbox requires PostgreSQL 15 or higher. Detected server_version_num: {versionNum}. Upgrade your database to prevent concurrency corruption.");
        }
    }

    /// <summary>
    /// Performs no operation; provided to satisfy the <see cref="IHostedService"/> contract.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A completed task.</returns>
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
