// Copyright © Erickson Lopez. MIT License.
using System;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.Outbox.Persistence;
using Microsoft.Extensions.Options;
using Npgsql;

namespace EricksonLopez.Outbox.Storage.PostgreSql;

/// <summary>
/// Provides a PostgreSQL implementation of <see cref="IIdempotencyRepository"/>.
/// </summary>
public sealed class PostgreSqlIdempotencyRepository : IIdempotencyRepository
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly string _insertSql;
    private readonly string _purgeSql;

    /// <summary>
    /// Initializes a new instance of the <see cref="PostgreSqlIdempotencyRepository"/> class.
    /// </summary>
    /// <param name="dataSource">The PostgreSQL data source.</param>
    /// <param name="options">The outbox runtime options.</param>
    /// <exception cref="ArgumentNullException"><paramref name="dataSource"/> or <paramref name="options"/> is <see langword="null"/>.</exception>
    [CLSCompliant(false)]

    public PostgreSqlIdempotencyRepository(NpgsqlDataSource dataSource, IOptionsMonitor<OutboxRuntimeOptions> options)
    {
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
        ArgumentNullException.ThrowIfNull(options);

        var schema = string.IsNullOrWhiteSpace(options.CurrentValue.SchemaName) ? "public" : options.CurrentValue.SchemaName;
        var table = options.CurrentValue.TableName + "_idempotency";
        var fullTableName = $"\"{schema}\".\"{table}\"";

        _insertSql = $@"
            INSERT INTO {fullTableName} (message_id, consumer_id, processed_at)
            VALUES (@MessageId, @ConsumerId, @ProcessedAt)
            ON CONFLICT (message_id, consumer_id) DO NOTHING;";

        _purgeSql = $"DELETE FROM {fullTableName} WHERE processed_at < @OlderThan;";
    }

    /// <inheritdoc/>
    public async ValueTask<bool> TryInsertAsync(IdempotencyRecord record, IOutboxTransactionContext? transaction = default, CancellationToken cancellationToken = default)
    {
        NpgsqlConnection? conn = null;
        bool closeConn = false;

        if (transaction != null)
        {
            conn = transaction.Connection as NpgsqlConnection;
            if (conn == null) throw new InvalidOperationException("Transaction must be associated with an NpgsqlConnection.");
        }
        else
        {
            conn = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            closeConn = true;
        }

        try
        {
            await using var cmd = new NpgsqlCommand(_insertSql, conn, transaction?.Transaction as NpgsqlTransaction);
            cmd.Parameters.Add(new NpgsqlParameter("MessageId", NpgsqlTypes.NpgsqlDbType.Varchar) { Value = record.MessageId });
            cmd.Parameters.Add(new NpgsqlParameter("ConsumerId", NpgsqlTypes.NpgsqlDbType.Varchar) { Value = record.ConsumerId });
            cmd.Parameters.Add(new NpgsqlParameter("ProcessedAt", NpgsqlTypes.NpgsqlDbType.TimestampTz) { Value = record.ProcessedAt });

            var count = await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return count > 0;
        }
        finally
        {
            if (closeConn && conn != null)
            {
                await conn.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    /// <inheritdoc/>
    public async ValueTask PurgeExpiredRecordsAsync(DateTimeOffset olderThan, CancellationToken cancellationToken = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand(_purgeSql, conn);
        cmd.Parameters.Add(new NpgsqlParameter("OlderThan", NpgsqlTypes.NpgsqlDbType.TimestampTz) { Value = olderThan });
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}



