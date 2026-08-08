using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.Outbox;

using EricksonLopez.Outbox.Persistence;
using Microsoft.Extensions.Options;

namespace EricksonLopez.Outbox.Storage.SqlServer;

/// <summary>
/// SQL Server implementation of <see cref="IIdempotencyRepository"/>.
/// </summary>
public sealed class SqlServerIdempotencyRepository : IIdempotencyRepository
{
    private readonly Func<IDbConnection> _connectionFactory;

    private readonly string _insertSql;
    private readonly string _purgeSql;

    /// <summary>
    /// Initializes a new instance of the <see cref="SqlServerIdempotencyRepository"/> class.
    /// </summary>
    /// <param name="connectionFactory">The factory that creates SQL Server connections.</param>
    /// <param name="options">The outbox runtime options.</param>
    /// <exception cref="ArgumentNullException"><paramref name="connectionFactory"/> or <paramref name="options"/> is <see langword="null"/>.</exception>
    public SqlServerIdempotencyRepository(Func<IDbConnection> connectionFactory, IOptionsMonitor<OutboxRuntimeOptions> options)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        ArgumentNullException.ThrowIfNull(options);

        var schema = string.IsNullOrWhiteSpace(options.CurrentValue.SchemaName) ? "dbo" : options.CurrentValue.SchemaName;
        var table = options.CurrentValue.TableName + "_idempotency";
        var fullTableName = $"[{schema}].[{table}]";

        _insertSql = $@"
            INSERT INTO {fullTableName} (message_id, consumer_id, processed_at)
            SELECT @MessageId, @ConsumerId, @ProcessedAt
            WHERE NOT EXISTS (
                SELECT 1 
                FROM {fullTableName} WITH (UPDLOCK, HOLDLOCK) 
                WHERE message_id = @MessageId AND consumer_id = @ConsumerId
            );";

        _purgeSql = $"DELETE FROM {fullTableName} WHERE processed_at < @OlderThan;";
    }

    /// <inheritdoc/>
    public async ValueTask<bool> TryInsertAsync(IdempotencyRecord record, EricksonLopez.Outbox.Persistence.IOutboxTransactionContext? transaction = default, CancellationToken cancellationToken = default)
    {
        Microsoft.Data.SqlClient.SqlConnection? conn = null;
        Microsoft.Data.SqlClient.SqlTransaction? tx = null;
        bool disposeConn = false;

        if (transaction != null)
        {
            conn = transaction.Connection as Microsoft.Data.SqlClient.SqlConnection ?? throw new System.InvalidOperationException("Connection must be a SqlConnection");
            tx = transaction.Transaction as Microsoft.Data.SqlClient.SqlTransaction;
        }
        else
        {
            conn = (Microsoft.Data.SqlClient.SqlConnection)_connectionFactory();
            disposeConn = true;
            await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        }

        try
        {
            using var cmd = new Microsoft.Data.SqlClient.SqlCommand(_insertSql, conn, tx);
            cmd.Parameters.AddWithValue("@MessageId", record.MessageId);
            cmd.Parameters.AddWithValue("@ConsumerId", record.ConsumerId);
            cmd.Parameters.AddWithValue("@ProcessedAt", record.ProcessedAt);
            var count = await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return count > 0;
        }
        finally
        {
            if (disposeConn) conn?.Dispose();
        }
    }

    /// <inheritdoc/>
    public async ValueTask PurgeExpiredRecordsAsync(DateTimeOffset olderThan, CancellationToken cancellationToken = default)
    {
        using var conn = (Microsoft.Data.SqlClient.SqlConnection)_connectionFactory();
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        using var cmd = new Microsoft.Data.SqlClient.SqlCommand(_purgeSql, conn);
        cmd.Parameters.AddWithValue("@OlderThan", olderThan);
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
