// Copyright © Erickson Lopez. MIT License.
using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.Outbox.Persistence;
using Microsoft.Extensions.Options;
using Oracle.ManagedDataAccess.Client;

namespace EricksonLopez.Outbox.Storage.Oracle;

/// <summary>
/// Provides an Oracle implementation of <see cref="IIdempotencyRepository"/>.
/// </summary>
public sealed class OracleIdempotencyRepository : IIdempotencyRepository
{
    private readonly Func<IDbConnection> _connectionFactory;
    private readonly string _insertSql;
    private readonly string _purgeSql;

    /// <summary>
    /// Initializes a new instance of the <see cref="OracleIdempotencyRepository"/> class.
    /// </summary>
    /// <param name="connectionFactory">The factory that creates Oracle connections.</param>
    /// <param name="options">The outbox runtime options.</param>
    /// <exception cref="ArgumentNullException"><paramref name="connectionFactory"/> or <paramref name="options"/> is <see langword="null"/>.</exception>

    public OracleIdempotencyRepository(Func<IDbConnection> connectionFactory, IOptionsMonitor<OutboxRuntimeOptions> options)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        ArgumentNullException.ThrowIfNull(options);

        // Stryker disable Conditional,String : Schema routing conditional and table name string per ADR-013
        var schema = string.IsNullOrWhiteSpace(options.CurrentValue.SchemaName) ? "" : options.CurrentValue.SchemaName.ToUpperInvariant();
        var table = (options.CurrentValue.TableName + "_IDEMPOTENCY").ToUpperInvariant();
        var fullTableName = string.IsNullOrEmpty(schema) ? $"\"{table}\"" : $"\"{schema}\".\"{table}\"";
        // Stryker restore Conditional,String

        _insertSql = $@"
            INSERT INTO {fullTableName} (message_id, consumer_id, processed_at)
            SELECT :MessageId, :ConsumerId, :ProcessedAt FROM DUAL
            WHERE NOT EXISTS (
                SELECT 1 
                FROM {fullTableName} 
                WHERE message_id = :MessageId AND consumer_id = :ConsumerId
            )";

        _purgeSql = $"DELETE FROM {fullTableName} WHERE processed_at < :OlderThan";
    }

    /// <inheritdoc/>
    public async ValueTask<bool> TryInsertAsync(IdempotencyRecord record, IOutboxTransactionContext? transaction = default, CancellationToken cancellationToken = default)
    {
        OracleConnection? conn = null;
        OracleTransaction? tx = null;
        bool disposeConn = false;

        if (transaction != null)
        {
            conn = (transaction.Connection as OracleConnection);
            tx = (transaction.Transaction as OracleTransaction);
        }
        else
        {
            conn = (OracleConnection)_connectionFactory();
            disposeConn = true;
            await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        }

        try
        {
            using var cmd = new OracleCommand(_insertSql, conn);
            // Stryker disable once Boolean : OracleCommand BindByName always true per ADR-013
            cmd.BindByName = true;
            // Stryker disable once Equality : Null transaction check per ADR-013
            if (tx != null)
            {
                // Stryker disable once Block : Conditional cmd.Transaction assignment per ADR-013
                cmd.Transaction = tx;
            }
            
            cmd.Parameters.Add(new OracleParameter("MessageId", OracleDbType.Varchar2) { Value = record.MessageId });
            cmd.Parameters.Add(new OracleParameter("ConsumerId", OracleDbType.Varchar2) { Value = record.ConsumerId });
            cmd.Parameters.Add(new OracleParameter("ProcessedAt", OracleDbType.TimeStampTZ) { Value = record.ProcessedAt });

            var count = await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return count > 0;
        }
        finally
        {
            // Stryker disable once Logical,Boolean : Connection lifecycle cleanup per ADR-013
            if (disposeConn && conn != null)
            {
                await conn.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    /// <inheritdoc/>
    public async ValueTask PurgeExpiredRecordsAsync(DateTimeOffset olderThan, CancellationToken cancellationToken = default)
    {
        using var conn = (OracleConnection)_connectionFactory();
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        using var cmd = new OracleCommand(_purgeSql, conn);
        // Stryker disable once Boolean : OracleCommand BindByName always true per ADR-013
        cmd.BindByName = true;
        cmd.Parameters.Add(new OracleParameter("OlderThan", OracleDbType.TimeStampTZ) { Value = olderThan });
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
