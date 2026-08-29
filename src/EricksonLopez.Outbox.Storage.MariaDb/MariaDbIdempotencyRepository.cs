// Copyright © Erickson Lopez. MIT License.
using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.Outbox.Persistence;
using Microsoft.Extensions.Options;
using MySqlConnector;

namespace EricksonLopez.Outbox.Storage.MariaDb;

/// <summary>
/// Provides a MariaDB implementation of <see cref="IIdempotencyRepository"/>.
/// </summary>
public sealed class MariaDbIdempotencyRepository : IIdempotencyRepository
{
    private readonly Func<IDbConnection> _connectionFactory;
    private readonly string _insertSql;
    private readonly string _purgeSql;

    /// <summary>
    /// Initializes a new instance of the <see cref="MariaDbIdempotencyRepository"/> class.
    /// </summary>
    /// <param name="connectionFactory">The factory that creates MariaDB connections.</param>
    /// <param name="options">The outbox runtime options.</param>
    /// <exception cref="ArgumentNullException"><paramref name="connectionFactory"/> or <paramref name="options"/> is <see langword="null"/>.</exception>

    public MariaDbIdempotencyRepository(Func<IDbConnection> connectionFactory, IOptionsMonitor<OutboxRuntimeOptions> options)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        ArgumentNullException.ThrowIfNull(options);

        var table = options.CurrentValue.TableName + "_idempotency";
        var fullTableName = string.IsNullOrWhiteSpace(options.CurrentValue.SchemaName)
            ? $"`{table}`"
            : $"`{options.CurrentValue.SchemaName}`.`{table}`";

        _insertSql = $@"
            INSERT IGNORE INTO {fullTableName} (message_id, consumer_id, processed_at)
            VALUES (@MessageId, @ConsumerId, @ProcessedAt);";

        _purgeSql = $"DELETE FROM {fullTableName} WHERE processed_at < @OlderThan;";
    }

    /// <inheritdoc/>
    public async ValueTask<bool> TryInsertAsync(IdempotencyRecord record, IOutboxTransactionContext? transaction = default, CancellationToken cancellationToken = default)
    {
        MySqlConnection? conn = null;
        MySqlTransaction? tx = null;
        bool disposeConn = false;

        if (transaction != null)
        {
            conn = (transaction.Connection as MySqlConnection);
            tx = (transaction.Transaction as MySqlTransaction);
        }
        else
        {
            conn = (MySqlConnection)_connectionFactory();
            disposeConn = true;
            await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        }

        try
        {
            using var cmd = new MySqlCommand(_insertSql, conn, tx);
            cmd.Parameters.AddWithValue("@MessageId", record.MessageId);
            cmd.Parameters.AddWithValue("@ConsumerId", record.ConsumerId);
            cmd.Parameters.AddWithValue("@ProcessedAt", record.ProcessedAt.UtcDateTime);

            var count = await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return count > 0;
        }
        finally
        {
            if (disposeConn && conn != null)
            {
                await conn.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    /// <inheritdoc/>
    public async ValueTask PurgeExpiredRecordsAsync(DateTimeOffset olderThan, CancellationToken cancellationToken = default)
    {
        using var conn = (MySqlConnection)_connectionFactory();
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        using var cmd = new MySqlCommand(_purgeSql, conn);
        cmd.Parameters.AddWithValue("@OlderThan", olderThan.UtcDateTime);
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
