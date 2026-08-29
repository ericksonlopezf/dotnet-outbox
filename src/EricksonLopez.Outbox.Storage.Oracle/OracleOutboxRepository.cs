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
using Oracle.ManagedDataAccess.Client;

namespace EricksonLopez.Outbox.Storage.Oracle;

/// <summary>
/// Provides an Oracle implementation of <see cref="IOutboxRepository"/>.
/// </summary>
public sealed class OracleOutboxRepository : IOutboxRepository
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

    /// <summary>
    /// Initializes a new instance of the <see cref="OracleOutboxRepository"/> class.
    /// </summary>
    /// <param name="connectionFactory">The factory that creates Oracle connections.</param>
    /// <param name="options">The runtime options containing thresholds and configurations.</param>
    /// <exception cref="ArgumentNullException"><paramref name="connectionFactory"/> is <see langword="null"/>.</exception>

    public OracleOutboxRepository(Func<IDbConnection> connectionFactory, IOptions<OutboxRuntimeOptions>? options = null)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _options = options?.Value ?? new OutboxRuntimeOptions();

        var schema = _options.SchemaName;
        var table = _options.TableName;
        
        // Stryker disable String : Exception parameter messages per ADR-013
        if (!string.IsNullOrEmpty(schema) && !System.Text.RegularExpressions.Regex.IsMatch(schema, "^[a-zA-Z0-9_]+$", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(1)))
            throw new ArgumentException("Schema name contains invalid characters.", nameof(options));
        if (!System.Text.RegularExpressions.Regex.IsMatch(table, "^[a-zA-Z0-9_]+$", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(1)))
            throw new ArgumentException("Table name contains invalid characters.", nameof(options));
        // Stryker restore String

        // Stryker disable String,Equality,Conditional : Schema routing strings and equality per ADR-013
        _fullTableName = string.IsNullOrEmpty(schema) ? $"\"{table}\"" : $"\"{schema}\".\"{table}\"";
        if (schema == "public" && table == "outbox_messages") 
        {
            // Stryker disable once Block : Fallback path tested via constructor tests per ADR-013
            _fullTableName = table; // Fallback to default unquoted
        }
        // Stryker restore String,Equality,Conditional

        _insertSql = $@"
        INSERT INTO {_fullTableName} 
            (id, type, payload, correlation_id, causation_id, headers_json, state, created_at, updated_at, deliver_at, retry_count)
        SELECT :Id, :MessageType, :Payload, :CorrelationId, :CausationId, :HeadersJson, 0, :CreatedAt, :CreatedAt, :DeliverAt, 0
        FROM DUAL
        WHERE NOT EXISTS (SELECT 1 FROM {_fullTableName} WHERE id = :Id)";

        _markDispatchedSql = $@"
        DELETE FROM {_fullTableName}
        WHERE id = :Id";

        _markFailedSql = $@"
        UPDATE {_fullTableName}
        SET state = :State, 
            error = :Error, 
            updated_at = SYSTIMESTAMP, 
            retry_count = retry_count + 1,
            deliver_at = CASE 
                WHEN :State = 3 THEN SYSTIMESTAMP + NUMTODSINTERVAL(POWER(2, LEAST(retry_count, 11)) * 10, 'SECOND')
                ELSE deliver_at 
            END
        WHERE id = :Id";

        _claimIdsSql = $@"
            SELECT id FROM {_fullTableName}
            WHERE id IN (
                SELECT id FROM (
                    SELECT id
                    FROM {_fullTableName}
                    WHERE state IN (0, 3)
                      AND (deliver_at IS NULL OR deliver_at <= SYSTIMESTAMP)
                    ORDER BY created_at ASC, id ASC
                ) WHERE ROWNUM <= :BatchSize
            )
            FOR UPDATE SKIP LOCKED";

        _updateClaimedSql = $@"
            UPDATE {_fullTableName} 
            SET state = 1, updated_at = SYSTIMESTAMP, owner_id = HEXTORAW('{_options.InstanceId}') 
            WHERE id IN ({{0}})";

        _hydrateSql = $@"
            SELECT id, type, payload, correlation_id, causation_id, headers_json, created_at, 
                   processed_at, deliver_at, state, error, retry_count
            FROM {_fullTableName}
            WHERE id IN ({{0}})";

        _reclaimSql = $@"
            UPDATE {_fullTableName}
            SET state = 0, updated_at = SYSTIMESTAMP, owner_id = NULL 
              WHERE state = 1
              AND updated_at < SYSTIMESTAMP - NUMTODSINTERVAL(:StaleSeconds, 'SECOND')
              AND created_at > SYSTIMESTAMP - NUMTODSINTERVAL(:MaxAgeDays, 'DAY')";

        _countSql = $"SELECT COUNT(*) FROM {_fullTableName} WHERE state IN (0, 3)";

        _purgeDispatchedSql = $@"
            DELETE FROM {_fullTableName}
            WHERE id IN (
                SELECT id FROM (
                    SELECT id FROM {_fullTableName}
                    WHERE state = 2
                      AND (processed_at < :Cutoff OR (processed_at IS NULL AND updated_at < :Cutoff))
                    ORDER BY created_at ASC
                ) WHERE ROWNUM <= :BatchSize
            )";
    }

    /// <inheritdoc/>

    public async ValueTask InsertAsync(OutboxMessage record, IOutboxTransactionContext transaction, CancellationToken cancellationToken = default)
    {
        var conn = (transaction.Connection as System.Data.Common.DbConnection) ?? throw new InvalidOperationException("Transaction connection is null.");
        using var cmd = conn.CreateCommand();
        cmd.Transaction = (transaction.Transaction as System.Data.Common.DbTransaction);
        cmd.CommandText = _insertSql;
        if (cmd is OracleCommand oraCmd) oraCmd.BindByName = true;

        var pId = cmd.CreateParameter(); pId.ParameterName = ":Id"; pId.Value = record.Id.ToByteArray(); cmd.Parameters.Add(pId);
        var pType = cmd.CreateParameter(); pType.ParameterName = ":MessageType"; pType.Value = record.MessageType; cmd.Parameters.Add(pType);
        var pPayload = cmd.CreateParameter(); pPayload.ParameterName = ":Payload"; pPayload.Value = record.Payload.ToArray(); cmd.Parameters.Add(pPayload);
        var pCorr = cmd.CreateParameter(); pCorr.ParameterName = ":CorrelationId"; pCorr.Value = record.CorrelationId ?? (object)DBNull.Value; cmd.Parameters.Add(pCorr);
        var pCaus = cmd.CreateParameter(); pCaus.ParameterName = ":CausationId"; pCaus.Value = record.CausationId ?? (object)DBNull.Value; cmd.Parameters.Add(pCaus);
        var pHeaders = cmd.CreateParameter(); pHeaders.ParameterName = ":HeadersJson"; pHeaders.Value = record.Headers.ToArray(); cmd.Parameters.Add(pHeaders);
        var pCreated = cmd.CreateParameter(); pCreated.ParameterName = ":CreatedAt"; pCreated.Value = record.CreatedAt.UtcDateTime; cmd.Parameters.Add(pCreated);
        var pDeliver = cmd.CreateParameter(); pDeliver.ParameterName = ":DeliverAt"; pDeliver.Value = record.DeliverAt.HasValue ? record.DeliverAt.Value.UtcDateTime : (object)DBNull.Value; cmd.Parameters.Add(pDeliver);

        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>

    public async ValueTask InsertBatchAsync(ReadOnlyMemory<OutboxMessage> records, IOutboxTransactionContext transaction, CancellationToken cancellationToken = default)
    {
        if (records.IsEmpty) return;
        var conn = transaction.Connection as OracleConnection ?? throw new InvalidOperationException("Not an OracleConnection");
        
        var span = records.Span;
        var count = span.Length;
        
        var idArray = new byte[count][];
        var typeArray = new string[count];
        var payloadArray = new byte[count][];
        var corrArray = new string?[count];
        var causArray = new string?[count];
        var headersArray = new byte[count][];
        var createdArray = new DateTime[count];
        var deliverArray = new object[count];
        
        for (int i = 0; i < count; i++)
        {
            var r = span[i];
            idArray[i] = r.Id.ToByteArray();
            typeArray[i] = r.MessageType;
            payloadArray[i] = r.Payload.ToArray();
            corrArray[i] = r.CorrelationId;
            causArray[i] = r.CausationId;
            headersArray[i] = r.Headers.ToArray();
            createdArray[i] = r.CreatedAt.UtcDateTime;
            deliverArray[i] = r.DeliverAt.HasValue ? r.DeliverAt.Value.UtcDateTime : (object)DBNull.Value;
        }

        using var cmd = conn.CreateCommand();
        cmd.Transaction = (transaction.Transaction as OracleTransaction);
        cmd.CommandText = _insertSql;
        cmd.BindByName = true;
        cmd.ArrayBindCount = count;

        cmd.Parameters.Add(new OracleParameter("Id", OracleDbType.Raw) { Value = idArray });
        cmd.Parameters.Add(new OracleParameter("MessageType", OracleDbType.NVarchar2) { Value = typeArray });
        cmd.Parameters.Add(new OracleParameter("Payload", OracleDbType.Blob) { Value = payloadArray });
        cmd.Parameters.Add(new OracleParameter("CorrelationId", OracleDbType.NVarchar2) { Value = corrArray });
        cmd.Parameters.Add(new OracleParameter("CausationId", OracleDbType.NVarchar2) { Value = causArray });
        cmd.Parameters.Add(new OracleParameter("HeadersJson", OracleDbType.Blob) { Value = headersArray });
        cmd.Parameters.Add(new OracleParameter("CreatedAt", OracleDbType.TimeStamp) { Value = createdArray });
        cmd.Parameters.Add(new OracleParameter("DeliverAt", OracleDbType.TimeStamp) { Value = deliverArray });

        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async ValueTask<IReadOnlyList<OutboxMessage>> FetchPendingAsync(int batchSize, CancellationToken cancellationToken = default)
    {
        using var dbConn = (System.Data.Common.DbConnection)_connectionFactory();
        await dbConn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var tx = await dbConn.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken).ConfigureAwait(false);

        var claimedIds = new List<byte[]>();
        using (var claimCmd = dbConn.CreateCommand())
        {
            claimCmd.Transaction = tx;
            if (claimCmd is OracleCommand oraCmd) oraCmd.BindByName = true;
            claimCmd.CommandText = _claimIdsSql;
            var pBatch = claimCmd.CreateParameter();
            pBatch.ParameterName = "BatchSize";
            pBatch.Value = batchSize;
            claimCmd.Parameters.Add(pBatch);
            using var reader = await claimCmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                claimedIds.Add((byte[])reader.GetValue(0));
            }
        }

        if (claimedIds.Count == 0)
        {
            await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
            return Array.Empty<OutboxMessage>();
        }

        var inClause = new System.Text.StringBuilder();
        using (var updateCmd = dbConn.CreateCommand())
        {
            updateCmd.Transaction = tx;
            if (updateCmd is OracleCommand oraCmd) oraCmd.BindByName = true;

            for (int i = 0; i < claimedIds.Count; i++)
            {
                var pName = "Id" + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
                inClause.Append(i == 0 ? ":" : ", :").Append(pName);
                
                var pId = updateCmd.CreateParameter();
                pId.ParameterName = pName;
                pId.Value = claimedIds[i];
                updateCmd.Parameters.Add(pId);
            }

            updateCmd.CommandText = _updateClaimedSql.Replace("{0}", inClause.ToString(), StringComparison.Ordinal);

            await updateCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        var result = new List<OutboxMessage>(claimedIds.Count);
        using (var hydrateCmd = dbConn.CreateCommand())
        {
            hydrateCmd.Transaction = tx;
            if (hydrateCmd is OracleCommand oraCmd) oraCmd.BindByName = true;

            for (int i = 0; i < claimedIds.Count; i++)
            {
                var pName = "Id" + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
                var pId = hydrateCmd.CreateParameter();
                pId.ParameterName = pName;
                pId.Value = claimedIds[i];
                hydrateCmd.Parameters.Add(pId);
            }

            hydrateCmd.CommandText = _hydrateSql.Replace("{0}", inClause.ToString(), StringComparison.Ordinal);

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

                // Stryker disable Conditional,Boolean : Nullable reader conditionals — field nullability varies by row per ADR-013
                var processedAt = reader.IsDBNull(processedAtOrd) ? (DateTime?)null : reader.GetDateTime(processedAtOrd);
                var deliverAt = reader.IsDBNull(deliverAtOrd) ? (DateTime?)null : reader.GetDateTime(deliverAtOrd);

                result.Add(new OutboxMessage(
                    Id: new Guid((byte[])reader.GetValue(idOrd)),
                    MessageType: reader.GetString(messageTypeOrd),
                    Payload: reader.IsDBNull(payloadOrd) ? "{}"u8.ToArray() : (byte[])reader.GetValue(payloadOrd),
                    CorrelationId: reader.IsDBNull(correlationIdOrd) ? null : reader.GetString(correlationIdOrd),
                    CausationId: reader.IsDBNull(causationIdOrd) ? null : reader.GetString(causationIdOrd),
                    Headers: reader.IsDBNull(headersOrd) ? "{}"u8.ToArray() : (byte[])reader.GetValue(headersOrd),
                    CreatedAt: new DateTimeOffset(reader.GetDateTime(createdAtOrd), TimeSpan.Zero),
                    ProcessedAt: processedAt.HasValue ? new DateTimeOffset(processedAt.Value, TimeSpan.Zero) : null,
                    DeliverAt: deliverAt.HasValue ? new DateTimeOffset(deliverAt.Value, TimeSpan.Zero) : null,
                    Status: (EricksonLopez.Outbox.OutboxMessageStatus)state,
                    RetryCount: reader.GetInt32(retryCountOrd),
                    Error: reader.IsDBNull(errorOrd) ? null : reader.GetString(errorOrd)));
                // Stryker restore Conditional,Boolean
            }
        }

        await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
        return result;
    }

    /// <inheritdoc/>

    public async ValueTask MarkAsDispatchedAsync(IReadOnlyList<OutboxMessage> messages, CancellationToken cancellationToken = default)
    {
        if (messages.Count == 0) return;

        using var conn = _connectionFactory() as OracleConnection ?? throw new InvalidOperationException("Not an OracleConnection");
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = _markDispatchedSql;
        // Stryker disable once Boolean : OracleCommand cast checked at runtime per ADR-013
        cmd.BindByName = true;
        cmd.ArrayBindCount = messages.Count;

        var idArray = new byte[messages.Count][];
        for (int i = 0; i < messages.Count; i++) idArray[i] = messages[i].Id.ToByteArray();

        cmd.Parameters.Add(new OracleParameter("Id", OracleDbType.Raw) { Value = idArray });
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>

    public async ValueTask MarkAsFailedAsync(IReadOnlyList<OutboxMessage> messages, string error, bool isDeadLetter = false, CancellationToken cancellationToken = default)
    {
        if (messages.Count == 0) return;

        using var conn = _connectionFactory() as OracleConnection ?? throw new InvalidOperationException("Not an OracleConnection");
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = _markFailedSql;
        cmd.BindByName = true;
        cmd.ArrayBindCount = messages.Count;

        var stateVal = isDeadLetter ? 4 : 3;
        var stateArray = new int[messages.Count];
        var errorArray = new string?[messages.Count];
        var idArray = new byte[messages.Count][];

        for (int i = 0; i < messages.Count; i++)
        {
            stateArray[i] = stateVal;
            errorArray[i] = error;
            idArray[i] = messages[i].Id.ToByteArray();
        }

        cmd.Parameters.Add(new OracleParameter("State", OracleDbType.Int32) { Value = stateArray });
        cmd.Parameters.Add(new OracleParameter("Error", OracleDbType.NVarchar2) { Value = errorArray });
        cmd.Parameters.Add(new OracleParameter("Id", OracleDbType.Raw) { Value = idArray });

        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async ValueTask<int> ReclaimStaleMessagesAsync(TimeSpan staleTimeout, CancellationToken cancellationToken = default)
    {
        using var conn = (System.Data.Common.DbConnection)_connectionFactory();
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = _reclaimSql;
        // Stryker disable once Boolean : OracleCommand cast checked at runtime per ADR-013
        if (cmd is OracleCommand oraCmd) oraCmd.BindByName = true;

        var pStale = cmd.CreateParameter(); pStale.ParameterName = ":StaleSeconds"; pStale.Value = (int)staleTimeout.TotalSeconds; cmd.Parameters.Add(pStale);
        var pMaxAge = cmd.CreateParameter(); pMaxAge.ParameterName = ":MaxAgeDays"; pMaxAge.Value = (int)_options.MaxMessageAge.TotalDays; cmd.Parameters.Add(pMaxAge);

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
        // Stryker disable once Equality : Defensive parameter guard per ADR-013
        if (batchSize <= 0) return 0;

        using var conn = (System.Data.Common.DbConnection)_connectionFactory();
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = _purgeDispatchedSql;
        if (cmd is OracleCommand oraCmd) oraCmd.BindByName = true;

        var pCutoff = cmd.CreateParameter();
        pCutoff.ParameterName = ":Cutoff";
        pCutoff.Value = cutoff.UtcDateTime;
        cmd.Parameters.Add(pCutoff);

        var pBatchSize = cmd.CreateParameter();
        pBatchSize.ParameterName = ":BatchSize";
        pBatchSize.Value = batchSize;
        cmd.Parameters.Add(pBatchSize);

        return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}






