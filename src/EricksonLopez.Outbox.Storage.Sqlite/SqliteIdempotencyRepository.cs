// Copyright © Erickson Lopez. MIT License.
using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.Outbox.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace EricksonLopez.Outbox.Storage.Sqlite;

/// <summary>
/// Provides an SQLite implementation of <see cref="IIdempotencyRepository"/>.
/// </summary>
public sealed class SqliteIdempotencyRepository : IIdempotencyRepository
{
    private readonly Func<IDbConnection> _connectionFactory;
    private readonly string _insertSql;
    private readonly string _purgeSql;

    /// <summary>
    /// Initializes a new instance of the <see cref="SqliteIdempotencyRepository"/> class.
    /// </summary>
    /// <param name="connectionFactory">The factory that creates SQLite connections.</param>
    /// <param name="options">The outbox runtime options.</param>
    /// <exception cref="ArgumentNullException"><paramref name="connectionFactory"/> or <paramref name="options"/> is <see langword="null"/>.</exception>

    public SqliteIdempotencyRepository(Func<IDbConnection> connectionFactory, IOptionsMonitor<OutboxRuntimeOptions> options)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        ArgumentNullException.ThrowIfNull(options);

        // SQLite doesn't use schemas, so we just use the table name.
        var fullTableName = $"\"{options.CurrentValue.TableName}_idempotency\"";

        _insertSql = $@"
            INSERT OR IGNORE INTO {fullTableName} (message_id, consumer_id, processed_at)
            VALUES (@MessageId, @ConsumerId, @ProcessedAt);";

        _purgeSql = $"DELETE FROM {fullTableName} WHERE processed_at < @OlderThan;";
    }

    /// <inheritdoc/>
    public async ValueTask<bool> TryInsertAsync(IdempotencyRecord record, IOutboxTransactionContext? transaction = default, CancellationToken cancellationToken = default)
    {
        SqliteConnection? conn = null;
        SqliteTransaction? tx = null;
        bool disposeConn = false;

        if (transaction != null)
        {
            conn = (transaction.Connection as SqliteConnection);
            tx = (transaction.Transaction as SqliteTransaction);
        }
        else
        {
            conn = (SqliteConnection)_connectionFactory();
            disposeConn = true;
            await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        }

        try
        {
            using var cmd = new SqliteCommand(_insertSql, conn, tx);
            cmd.Parameters.AddWithValue("@MessageId", record.MessageId);
            cmd.Parameters.AddWithValue("@ConsumerId", record.ConsumerId);
            
            // Format DateTimeOffset as ISO 8601 for SQLite since it lacks a native datetime type
            cmd.Parameters.AddWithValue("@ProcessedAt", record.ProcessedAt.ToString("O"));

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
        using var conn = (SqliteConnection)_connectionFactory();
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        using var cmd = new SqliteCommand(_purgeSql, conn);
        cmd.Parameters.AddWithValue("@OlderThan", olderThan.ToString("O"));
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}



