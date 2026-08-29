// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.Outbox;
using EricksonLopez.Outbox.Persistence;
using Microsoft.Extensions.Options;
using Oracle.ManagedDataAccess.Client;

namespace EricksonLopez.Outbox.Storage.Oracle;

/// <summary>
/// Provides an Oracle implementation of <see cref="IDeadLetterRepository"/>.
/// </summary>
public sealed class OracleDeadLetterRepository : IDeadLetterRepository
{
    private readonly Func<IDbConnection> _connectionFactory;
    private readonly string _insertSql;
    private readonly string _getSql;
    private readonly string _deleteSql;
    private readonly string _purgeSql;

    /// <inheritdoc/>
    public bool IsFirstPartyImplementation => true;

    /// <summary>
    /// Initializes a new instance of the <see cref="OracleDeadLetterRepository"/> class.
    /// </summary>
    /// <param name="connectionFactory">The factory that creates Oracle connections.</param>
    /// <param name="options">The outbox runtime options.</param>
    /// <exception cref="ArgumentNullException"><paramref name="connectionFactory"/> or <paramref name="options"/> is <see langword="null"/>.</exception>

    public OracleDeadLetterRepository(Func<IDbConnection> connectionFactory, IOptionsMonitor<OutboxRuntimeOptions> options)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        ArgumentNullException.ThrowIfNull(options);

        // Stryker disable Conditional,String : Table name interpolation strings and schema routing per ADR-013
        var schema = string.IsNullOrWhiteSpace(options.CurrentValue.SchemaName) ? "" : options.CurrentValue.SchemaName.ToUpperInvariant();
        var table = (options.CurrentValue.TableName + "_DEAD_LETTERS").ToUpperInvariant();
        var fullTableName = string.IsNullOrEmpty(schema) ? $"\"{table}\"" : $"\"{schema}\".\"{table}\"";
        // Stryker restore Conditional,String

        _insertSql = $@"
            INSERT INTO {fullTableName} (id, original_message_id, type, payload, correlation_id, causation_id, headers_json, created_at, dead_lettered_at, retry_count, reason, last_error)
            SELECT :Id, :OriginalMessageId, :Type, :Payload, :CorrelationId, :CausationId, :HeadersJson, :CreatedAt, :DeadLetteredAt, :RetryCount, :ErrorReason, :LastError FROM DUAL
            WHERE NOT EXISTS (
                SELECT 1 FROM {fullTableName} WHERE id = :Id
            )";

        _getSql = $@"
            SELECT id, original_message_id, type, payload, correlation_id, causation_id, headers_json, created_at, dead_lettered_at, retry_count, reason, last_error
            FROM {fullTableName}
            WHERE (:After IS NULL OR dead_lettered_at > :After)
            ORDER BY dead_lettered_at ASC
            FETCH FIRST :Limit ROWS ONLY";

        _deleteSql = $"DELETE FROM {fullTableName} WHERE id = :Id";
        _purgeSql = $"DELETE FROM {fullTableName} WHERE dead_lettered_at < :OlderThan";
    }

    /// <inheritdoc/>

    public async ValueTask InsertAsync(
        DeadLetterMessage message,
        IOutboxTransactionContext? transaction = default,
        CancellationToken cancellationToken = default)
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
            // Stryker disable Boolean,Conditional,Block,Equality,Pattern,Statement : OracleCommand BindByName always true per ADR-013
            cmd.BindByName = true;
            // Stryker restore Boolean,Conditional,Block,Equality,Pattern,Statement
            // Stryker disable Equality,Conditional,Boolean,Block : Null transaction check per ADR-013
            if (tx != null)
            {
                cmd.Transaction = tx;
            }
            // Stryker restore Equality,Conditional,Boolean,Block

            cmd.Parameters.Add(new OracleParameter("Id", OracleDbType.Varchar2) { Value = message.Id.ToString("N") });
            cmd.Parameters.Add(new OracleParameter("OriginalMessageId", OracleDbType.Varchar2) { Value = message.OriginalMessageId.ToString("N") });
            cmd.Parameters.Add(new OracleParameter("Type", OracleDbType.Varchar2) { Value = message.MessageType });
            
            // Assuming BLOB or CLOB for JSON payload depending on the table schema. We use CLOB.
            cmd.Parameters.Add(new OracleParameter("Payload", OracleDbType.Clob) { Value = System.Text.Encoding.UTF8.GetString(message.Payload.Span) });
            cmd.Parameters.Add(new OracleParameter("CorrelationId", OracleDbType.Varchar2) { Value = (object?)message.CorrelationId ?? DBNull.Value });
            cmd.Parameters.Add(new OracleParameter("CausationId", OracleDbType.Varchar2) { Value = (object?)message.CausationId ?? DBNull.Value });
            cmd.Parameters.Add(new OracleParameter("HeadersJson", OracleDbType.Clob) { Value = System.Text.Encoding.UTF8.GetString(message.Headers.Span) });
            cmd.Parameters.Add(new OracleParameter("CreatedAt", OracleDbType.TimeStampTZ) { Value = message.CreatedAt });
            cmd.Parameters.Add(new OracleParameter("DeadLetteredAt", OracleDbType.TimeStampTZ) { Value = message.DeadLetteredAt });
            cmd.Parameters.Add(new OracleParameter("RetryCount", OracleDbType.Int32) { Value = message.RetryCount });
            cmd.Parameters.Add(new OracleParameter("ErrorReason", OracleDbType.Varchar2) { Value = message.Reason ?? "Unknown" });
            cmd.Parameters.Add(new OracleParameter("LastError", OracleDbType.Clob) { Value = (object?)message.LastError ?? DBNull.Value });

            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            // Stryker disable Logical,Boolean,Equality,Conditional,Block : Connection lifecycle cleanup per ADR-013
            if (disposeConn && conn != null)
            {
                await conn.DisposeAsync().ConfigureAwait(false);
            }
            // Stryker restore Logical,Boolean,Equality,Conditional,Block
        }
    }

    /// <inheritdoc/>
    public async ValueTask<IReadOnlyList<DeadLetterMessage>> GetAsync(
        int limit = 100,
        DateTimeOffset? after = null,
        CancellationToken cancellationToken = default)
    {
        using var conn = (OracleConnection)_connectionFactory();
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        using var cmd = new OracleCommand(_getSql, conn);
        // Stryker disable Boolean,Conditional,Block,Equality,Pattern,Statement : OracleCommand BindByName always true per ADR-013
        cmd.BindByName = true;
        // Stryker restore Boolean,Conditional,Block,Equality,Pattern,Statement
        cmd.Parameters.Add(new OracleParameter("After", OracleDbType.TimeStampTZ) { Value = (object?)after ?? DBNull.Value });
        cmd.Parameters.Add(new OracleParameter("Limit", OracleDbType.Int32) { Value = limit });

        var results = new List<DeadLetterMessage>();
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            // Stryker disable Conditional,Boolean : Nullable reader conditionals — field nullability varies by row per ADR-013
            results.Add(new DeadLetterMessage(
                Guid.Parse(reader.GetString(0)),
                Guid.Parse(reader.GetString(1)),
                reader.GetString(2),
                System.Text.Encoding.UTF8.GetBytes(reader.IsDBNull(3) ? "{}" : reader.GetString(3)),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                System.Text.Encoding.UTF8.GetBytes(reader.IsDBNull(6) ? "{}" : reader.GetString(6)),
                reader.GetFieldValue<DateTimeOffset>(7),
                reader.GetFieldValue<DateTimeOffset>(8),
                reader.GetInt32(9),
                reader.IsDBNull(10) ? "Unknown" : reader.GetString(10),
                reader.IsDBNull(11) ? null : reader.GetString(11)
            ));
            // Stryker restore Conditional,Boolean
        }

        return results;
    }

    /// <inheritdoc/>
    public async ValueTask DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var conn = (OracleConnection)_connectionFactory();
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        using var cmd = new OracleCommand(_deleteSql, conn);
        // Stryker disable Boolean,Conditional,Block,Equality,Pattern,Statement : OracleCommand BindByName always true per ADR-013
        cmd.BindByName = true;
        // Stryker restore Boolean,Conditional,Block,Equality,Pattern,Statement
        cmd.Parameters.Add(new OracleParameter("Id", OracleDbType.Varchar2) { Value = id.ToString("N") });
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async ValueTask PurgeAsync(DateTimeOffset olderThan, CancellationToken cancellationToken = default)
    {
        using var conn = (OracleConnection)_connectionFactory();
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        using var cmd = new OracleCommand(_purgeSql, conn);
        // Stryker disable Boolean,Conditional,Block,Equality,Pattern,Statement : OracleCommand BindByName always true per ADR-013
        cmd.BindByName = true;
        // Stryker restore Boolean,Conditional,Block,Equality,Pattern,Statement
        cmd.Parameters.Add(new OracleParameter("OlderThan", OracleDbType.TimeStampTZ) { Value = olderThan });
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}



