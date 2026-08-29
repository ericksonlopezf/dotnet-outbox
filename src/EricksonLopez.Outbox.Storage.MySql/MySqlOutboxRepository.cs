// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.Outbox;
using EricksonLopez.Outbox.Persistence;
using EricksonLopez.Result;
using Microsoft.Extensions.Options;
using MySqlConnector;

namespace EricksonLopez.Outbox.Storage.MySql;

/// <summary>
/// Provides a MySQL implementation of <see cref="IOutboxRepository"/>.
/// </summary>
public sealed class MySqlOutboxRepository : IOutboxRepository
{
    private readonly Func<IDbConnection> _connectionFactory;
    private readonly OutboxRuntimeOptions _options;

    private readonly string _insertSql;
    private readonly string _markDispatchedSql;
    private readonly string _markFailedSql;
    private readonly string _claimIdsSql;
    private readonly string _updateClaimedSql;
    private readonly string _hydrateSql;
    private readonly string _reclaimSql;
    private readonly string _countSql;
    private readonly string _purgeDispatchedSql;
    private readonly string _fullTableName;
    private readonly string _destinationTableName;

    /// <summary>
    /// Initializes a new instance of the <see cref="MySqlOutboxRepository"/> class.
    /// </summary>
    /// <param name="connectionFactory">The factory that creates MySQL connections.</param>
    /// <param name="options">The runtime options containing thresholds and configurations.</param>
    /// <exception cref="ArgumentNullException"><paramref name="connectionFactory"/> is <see langword="null"/>.</exception>

    public MySqlOutboxRepository(Func<IDbConnection> connectionFactory, IOptions<OutboxRuntimeOptions>? options = null)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _options = options?.Value ?? new OutboxRuntimeOptions();

        var schema = _options.SchemaName;
        var table = _options.TableName;
        
        // Stryker disable all 
        if (!System.Text.RegularExpressions.Regex.IsMatch(schema, "^[a-zA-Z0-9_]+$", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(1)))
            throw new ArgumentException("Schema name contains invalid characters.", nameof(options));
        if (!System.Text.RegularExpressions.Regex.IsMatch(table, "^[a-zA-Z0-9_]+$", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(1)))
            throw new ArgumentException("Table name contains invalid characters.", nameof(options));
        // Stryker restore all

        _fullTableName = $"`{schema}`.`{table}`";
        _destinationTableName = $"{schema}.{table}";
        if (schema == "public" && table == "outbox_messages") 
        {
            _fullTableName = $"`{table}`";
            _destinationTableName = table;
        }

        _insertSql = $@"
        INSERT IGNORE INTO {_fullTableName} 
            (id, type, payload, correlation_id, causation_id, headers_json, state, created_at, updated_at, deliver_at, retry_count)
        VALUES 
            (@Id, @MessageType, @Payload, @CorrelationId, @CausationId, @HeadersJson, 0, @CreatedAt, @CreatedAt, @DeliverAt, 0);";

        _markDispatchedSql = $@"
        DELETE FROM {_fullTableName}
        WHERE id IN @Ids;";

        _markFailedSql = $@"
        UPDATE {_fullTableName}
        SET state = @State, 
            error = @Error, 
            updated_at = UTC_TIMESTAMP(), 
            retry_count = retry_count + 1,
            deliver_at = CASE 
                WHEN @State = 3 THEN DATE_ADD(UTC_TIMESTAMP(), INTERVAL (POWER(2, LEAST(retry_count, 11)) * 10) SECOND)
                ELSE deliver_at 
            END
        WHERE id IN @Ids;";

        _claimIdsSql = $@"
            SELECT id
            FROM {_fullTableName}
            WHERE state IN (0, 3)
              AND (deliver_at IS NULL OR deliver_at <= UTC_TIMESTAMP())
            ORDER BY created_at ASC, id ASC
            LIMIT @BatchSize
            FOR UPDATE SKIP LOCKED;";

        _updateClaimedSql = $@"
            UPDATE {_fullTableName} 
            SET state = 1, updated_at = UTC_TIMESTAMP(), owner_id = '{_options.InstanceId}' 
            WHERE id IN ({{0}});";

        _hydrateSql = $@"
            SELECT id, type, payload, correlation_id, causation_id, headers_json, created_at, 
                   processed_at, deliver_at, state, error, retry_count
            FROM {_fullTableName}
            WHERE id IN ({{0}});";

        _reclaimSql = $@"
            UPDATE {_fullTableName}
            SET state = 0, updated_at = UTC_TIMESTAMP(), owner_id = NULL 
                WHERE state = 1
              AND updated_at < DATE_SUB(UTC_TIMESTAMP(), INTERVAL @StaleSeconds SECOND)
              AND created_at > DATE_SUB(UTC_TIMESTAMP(), INTERVAL @MaxAgeDays DAY);";

        _countSql = $"SELECT COUNT(*) FROM {_fullTableName} WHERE state IN (0, 3);";

        _purgeDispatchedSql = $@"
            DELETE FROM {_fullTableName}
            WHERE state = 2
              AND (processed_at < @Cutoff OR (processed_at IS NULL AND updated_at < @Cutoff))
            ORDER BY created_at ASC
            LIMIT @BatchSize;";
    }

    /// <inheritdoc/>

    public async ValueTask InsertAsync(OutboxMessage record, IOutboxTransactionContext transaction, CancellationToken cancellationToken = default)
    {
        var conn = (transaction.Connection as System.Data.Common.DbConnection) ?? throw new InvalidOperationException("Transaction connection is null.");
        using var cmd = conn.CreateCommand();
        cmd.Transaction = (transaction.Transaction as System.Data.Common.DbTransaction);
        cmd.CommandText = _insertSql;
        
        cmd.Parameters.Add(new MySqlParameter("@Id", record.Id));
        cmd.Parameters.Add(new MySqlParameter("@MessageType", record.MessageType));
        cmd.Parameters.Add(new MySqlParameter("@Payload", record.Payload.ToArray()));
        cmd.Parameters.Add(new MySqlParameter("@CorrelationId", record.CorrelationId ?? (object)DBNull.Value));
        cmd.Parameters.Add(new MySqlParameter("@CausationId", record.CausationId ?? (object)DBNull.Value));
        cmd.Parameters.Add(new MySqlParameter("@HeadersJson", record.Headers.ToArray()));
        cmd.Parameters.Add(new MySqlParameter("@CreatedAt", record.CreatedAt.UtcDateTime));
        cmd.Parameters.Add(new MySqlParameter("@DeliverAt", record.DeliverAt.HasValue ? record.DeliverAt.Value.UtcDateTime : (object)DBNull.Value));
        
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>

    public async ValueTask InsertBatchAsync(ReadOnlyMemory<OutboxMessage> records, IOutboxTransactionContext transaction, CancellationToken cancellationToken = default)
    {
        if (records.IsEmpty) return;
        var conn = transaction.Connection as MySqlConnection 
                   ?? throw new InvalidOperationException("Transaction connection is not a MySqlConnection.");
        var mySqlTx = transaction.Transaction as MySqlTransaction;

        var bulkCopy = new MySqlBulkCopy(conn, mySqlTx);
        bulkCopy.DestinationTableName = _destinationTableName;

        // Stryker disable all 
        bulkCopy.ColumnMappings.Add(new MySqlBulkCopyColumnMapping(0, "id"));
        bulkCopy.ColumnMappings.Add(new MySqlBulkCopyColumnMapping(1, "type"));
        bulkCopy.ColumnMappings.Add(new MySqlBulkCopyColumnMapping(2, "payload"));
        bulkCopy.ColumnMappings.Add(new MySqlBulkCopyColumnMapping(3, "correlation_id"));
        bulkCopy.ColumnMappings.Add(new MySqlBulkCopyColumnMapping(4, "causation_id"));
        bulkCopy.ColumnMappings.Add(new MySqlBulkCopyColumnMapping(5, "headers_json"));
        bulkCopy.ColumnMappings.Add(new MySqlBulkCopyColumnMapping(6, "state"));
        bulkCopy.ColumnMappings.Add(new MySqlBulkCopyColumnMapping(7, "created_at"));
        bulkCopy.ColumnMappings.Add(new MySqlBulkCopyColumnMapping(8, "updated_at"));
        bulkCopy.ColumnMappings.Add(new MySqlBulkCopyColumnMapping(9, "deliver_at"));
        bulkCopy.ColumnMappings.Add(new MySqlBulkCopyColumnMapping(10, "retry_count"));

        using var table = new System.Data.DataTable();
        table.Columns.Add("id", typeof(Guid));
        table.Columns.Add("type", typeof(string));
        table.Columns.Add("payload", typeof(byte[]));
        table.Columns.Add("correlation_id", typeof(string));
        table.Columns.Add("causation_id", typeof(string));
        table.Columns.Add("headers_json", typeof(byte[]));
        table.Columns.Add("state", typeof(int));
        table.Columns.Add("created_at", typeof(DateTime));
        table.Columns.Add("updated_at", typeof(DateTime));
        table.Columns.Add("deliver_at", typeof(DateTime));
        table.Columns.Add("retry_count", typeof(int));
        // Stryker restore all

        for (int i = 0; i < records.Length; i++)
        {
            var r = records.Span[i];
            table.Rows.Add(
                r.Id, 
                r.MessageType, 
                r.Payload.ToArray(), 
                (object?)r.CorrelationId ?? DBNull.Value, 
                (object?)r.CausationId ?? DBNull.Value, 
                r.Headers.ToArray(), 
                r.Status, 
                r.CreatedAt.UtcDateTime, 
                r.CreatedAt.UtcDateTime, 
                r.DeliverAt.HasValue ? (object)r.DeliverAt.Value.UtcDateTime : DBNull.Value,
                0);
        }

        await bulkCopy.WriteToServerAsync(table, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async ValueTask<IReadOnlyList<OutboxMessage>> FetchPendingAsync(int batchSize, CancellationToken cancellationToken = default)
    {
        using var conn = _connectionFactory();
        var dbConn = (System.Data.Common.DbConnection)conn;
        await dbConn.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var tx = await dbConn.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken).ConfigureAwait(false);


        var claimedIds = new List<string>();
        using (var cmd = dbConn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = _claimIdsSql;
            cmd.Parameters.Add(new MySqlParameter("@BatchSize", batchSize));
            
            using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                claimedIds.Add(reader.GetGuid(0).ToString());
            }
        }

        if (claimedIds.Count == 0)
        {
            await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
            return Array.Empty<OutboxMessage>();
        }

        using (var updateCmd = dbConn.CreateCommand())
        {
            updateCmd.Transaction = tx;
            var paramNames = new string[claimedIds.Count];
            for (int i = 0; i < claimedIds.Count; i++)
            {
                var pName = $"@p{i}";
                paramNames[i] = pName;
                var p = updateCmd.CreateParameter();
                p.ParameterName = pName;
                p.Value = claimedIds[i].ToString();
                updateCmd.Parameters.Add(p);
            }
            var paramInClause = string.Join(",", paramNames);
            updateCmd.CommandText = _updateClaimedSql.Replace("{0}", paramInClause, StringComparison.Ordinal);
            await updateCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        // hydrateSql logic continues using string.Format in _hydrateSql

        var result = new List<OutboxMessage>(claimedIds.Count);
        using (var hydrateCmd = dbConn.CreateCommand())
        {
            hydrateCmd.Transaction = tx;
            var paramNames = new string[claimedIds.Count];
            for (int i = 0; i < claimedIds.Count; i++)
            {
                var pName = $"@p{i}";
                paramNames[i] = pName;
                var p = hydrateCmd.CreateParameter();
                p.ParameterName = pName;
                p.Value = claimedIds[i].ToString();
                hydrateCmd.Parameters.Add(p);
            }
            var paramInClause = string.Join(",", paramNames);
            hydrateCmd.CommandText = _hydrateSql.Replace("{0}", paramInClause, StringComparison.Ordinal);

            using var reader = await hydrateCmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!reader.HasRows) return result;
            
            var idOrd = reader.GetOrdinal("id");
            var messageTypeOrd = reader.GetOrdinal("type");
            var payloadOrd = reader.GetOrdinal("payload");
            var correlationIdOrd = reader.GetOrdinal("correlation_id");
            var causationIdOrd = reader.GetOrdinal("causation_id");
            var headersOrd = reader.GetOrdinal("headers_json");
            var createdAtOrd = reader.GetOrdinal("created_at");
            var processedAtOrd = reader.GetOrdinal("processed_at");
            var deliverAtOrd = reader.GetOrdinal("deliver_at");
            var stateOrd = reader.GetOrdinal("state");
            var errorOrd = reader.GetOrdinal("error");
            var retryCountOrd = reader.GetOrdinal("retry_count");

            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var state = (OutboxMessageStatus)reader.GetInt32(stateOrd);

                var processedAt = reader.IsDBNull(processedAtOrd) ? (DateTime?)null : reader.GetDateTime(processedAtOrd);
                var deliverAt = reader.IsDBNull(deliverAtOrd) ? (DateTime?)null : reader.GetDateTime(deliverAtOrd);

                result.Add(new OutboxMessage(
                    Id: reader.GetGuid(idOrd),
                    MessageType: reader.GetString(messageTypeOrd),
                    Payload: reader.GetFieldValue<byte[]>(payloadOrd),
                    CorrelationId: reader.IsDBNull(correlationIdOrd) ? null : reader.GetString(correlationIdOrd),
                    CausationId: reader.IsDBNull(causationIdOrd) ? null : reader.GetString(causationIdOrd),
                    Headers: reader.GetFieldValue<byte[]>(headersOrd),
                    CreatedAt: new DateTimeOffset(reader.GetDateTime(createdAtOrd), TimeSpan.Zero),
                    ProcessedAt: processedAt.HasValue ? new DateTimeOffset(processedAt.Value, TimeSpan.Zero) : null,
                    DeliverAt: deliverAt.HasValue ? new DateTimeOffset(deliverAt.Value, TimeSpan.Zero) : null,
                    Status: (EricksonLopez.Outbox.OutboxMessageStatus)state,
                    RetryCount: reader.GetInt32(retryCountOrd),
                    Error: reader.IsDBNull(errorOrd) ? null : reader.GetString(errorOrd)));
            }
        }

        await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
        return result;
    }

    /// <inheritdoc/>

    public async ValueTask MarkAsDispatchedAsync(IReadOnlyList<OutboxMessage> messages, CancellationToken cancellationToken = default)
    {
        if (messages.Count == 0) return;
        var inClauseBuilder = new System.Text.StringBuilder();
        for (int i = 0; i < messages.Count; i++)
        {
            if (i > 0) inClauseBuilder.Append(',');
            inClauseBuilder.Append('\'').Append(messages[i].Id.ToString()).Append('\'');
        }
        var inClause = inClauseBuilder.ToString();

        using var conn = (System.Data.Common.DbConnection)_connectionFactory();
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = _markDispatchedSql.Replace("@Ids", $"({inClause})");
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>

    public async ValueTask MarkAsFailedAsync(IReadOnlyList<OutboxMessage> messages, string error, bool isDeadLetter = false, CancellationToken cancellationToken = default)
    {
        if (messages.Count == 0) return;
        var inClauseBuilder = new System.Text.StringBuilder();
        for (int i = 0; i < messages.Count; i++)
        {
            if (i > 0) inClauseBuilder.Append(',');
            inClauseBuilder.Append('\'').Append(messages[i].Id.ToString()).Append('\'');
        }
        var inClause = inClauseBuilder.ToString();

        using var conn = (System.Data.Common.DbConnection)_connectionFactory();
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        
        using var cmd = conn.CreateCommand();
        cmd.CommandText = _markFailedSql.Replace("@Ids", $"({inClause})");
        cmd.Parameters.Add(new MySqlParameter("@State", isDeadLetter ? 4 : 3));
        cmd.Parameters.Add(new MySqlParameter("@Error", error ?? (object)DBNull.Value));
        
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async ValueTask<int> ReclaimStaleMessagesAsync(TimeSpan staleTimeout, CancellationToken cancellationToken = default)
    {
        using var conn = (System.Data.Common.DbConnection)_connectionFactory();
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = _reclaimSql;
        cmd.Parameters.Add(new MySqlParameter("@StaleSeconds", (int)staleTimeout.TotalSeconds));
        cmd.Parameters.Add(new MySqlParameter("@MaxAgeDays", (int)_options.MaxMessageAge.TotalDays));
        
        return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async ValueTask<long> GetPendingCountAsync(CancellationToken cancellationToken = default)
    {
        using var conn = (System.Data.Common.DbConnection)_connectionFactory();
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = _countSql;
        
        var result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return Convert.ToInt32(result, System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <inheritdoc/>

    public async ValueTask<int> PurgeDispatchedMessagesAsync(
        DateTimeOffset cutoff,
        int batchSize = 1000,
        CancellationToken cancellationToken = default)
    {
        // Stryker disable once all 
        if (batchSize <= 0) return 0;

        using var conn = (System.Data.Common.DbConnection)_connectionFactory();
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = _purgeDispatchedSql;

        var pCutoff = cmd.CreateParameter();
        pCutoff.ParameterName = "@Cutoff";
        pCutoff.Value = cutoff.UtcDateTime;
        cmd.Parameters.Add(pCutoff);

        var pBatchSize = cmd.CreateParameter();
        pBatchSize.ParameterName = "@BatchSize";
        pBatchSize.Value = batchSize;
        cmd.Parameters.Add(pBatchSize);

        return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}







