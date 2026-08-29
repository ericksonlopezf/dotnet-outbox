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

namespace EricksonLopez.Outbox.Storage.SqlServer;

/// <summary>
/// Provides a SQL Server implementation of <see cref="IOutboxRepository"/>.
/// Uses <c>WITH (UPDLOCK, READPAST)</c> for concurrent polling.
/// </summary>
/// <remarks>
/// Concurrency semantics:
///   - <c>READPAST</c> + <c>UPDLOCK</c> is the SQL Server equivalent of PostgreSQL's <c>FOR UPDATE SKIP LOCKED</c>
///     when the isolation level is <c>READ COMMITTED</c> or <c>REPEATABLE READ</c>.
///   - Under <c>SERIALIZABLE</c> isolation, READPAST silently fails (no rows are skipped).
///   - SQL Server 2022+ natively supports <c>SKIP LOCKED</c>; consider migrating for strict equivalence.
///   - Unlike PostgreSQL SKIP LOCKED, READPAST can skip non-locked rows on a locked PAGE in certain
///     page-level locking scenarios. Production use requires READ COMMITTED isolation.
/// </remarks>
public sealed class SqlServerOutboxRepository : IOutboxRepository
{
    private readonly Func<IDbConnection> _connectionFactory;
    private readonly OutboxRuntimeOptions _options;

    private readonly string _insertSql;
    private readonly string _fetchPendingSql;
    private readonly string _markDispatchedSql;
    private readonly string _markFailedSql;
    private readonly string _reclaimSql;
    private readonly string _countSql;
    private readonly string _purgeDispatchedSql;
    private readonly string _destinationTableName;
    private readonly Guid _instanceId;

    // FIX-01: Use CTE with ORDER BY to guarantee FIFO ordering.
    // UPDATE TOP does not support ORDER BY, breaking message causality.
    /// <summary>
    /// Initializes a new instance of the <see cref="SqlServerOutboxRepository"/> class.
    /// </summary>
    /// <param name="connectionFactory">The factory that creates SQL Server connections.</param>
    /// <param name="options">The runtime options containing thresholds and configurations.</param>
    /// <exception cref="ArgumentNullException"><paramref name="connectionFactory"/> is <see langword="null"/>.</exception>

    public SqlServerOutboxRepository(Func<IDbConnection> connectionFactory, IOptions<OutboxRuntimeOptions>? options = null)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _options = options?.Value ?? new OutboxRuntimeOptions();

        var schema = _options.SchemaName;
        var table = _options.TableName;
        
        if (!System.Text.RegularExpressions.Regex.IsMatch(schema, "^[a-zA-Z0-9_]+$", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(1)))
            throw new ArgumentException("Schema name contains invalid characters.", nameof(options));
        if (!System.Text.RegularExpressions.Regex.IsMatch(table, "^[a-zA-Z0-9_]+$", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(1)))
            throw new ArgumentException("Table name contains invalid characters.", nameof(options));

        var fullTableName = $"[{schema}].[{table}]";
        _destinationTableName = $"{schema}.{table}"; // SqlBulkCopy uses this format without brackets if schema is specified, but brackets are safer.

        _instanceId = Guid.Parse(_options.InstanceId);

        _insertSql = $@"
        INSERT INTO {fullTableName} (id, type, payload, correlation_id, causation_id, headers_json, state, created_at, updated_at, deliver_at, retry_count)
        SELECT @Id, @MessageType, @Payload, @CorrelationId, @CausationId, @HeadersJson, 0, @CreatedAt, @CreatedAt, @DeliverAt, 0
        WHERE NOT EXISTS (SELECT 1 FROM {fullTableName} WITH (UPDLOCK, HOLDLOCK) WHERE id = @Id);";

        _fetchPendingSql = $@"
        WITH batch AS (
            SELECT TOP (@BatchSize) id, created_at
            FROM {fullTableName} WITH (UPDLOCK, READPAST)
            WHERE state IN (0, 3)
              AND (deliver_at IS NULL OR deliver_at <= SYSDATETIMEOFFSET())
            ORDER BY created_at ASC, id ASC
        )
        UPDATE m
        SET    m.state      = 1,
               m.updated_at = SYSDATETIMEOFFSET(),
               m.owner_id   = @OwnerId
        OUTPUT inserted.id           AS Id,
               inserted.type         AS MessageType,
               inserted.payload      AS Payload,
               inserted.correlation_id AS CorrelationId,
               inserted.causation_id  AS CausationId,
               inserted.headers_json  AS HeadersJson,
               inserted.created_at    AS CreatedAt,
               inserted.processed_at  AS ProcessedAt,
               inserted.deliver_at    AS DeliverAt,
               inserted.state         AS State,
               inserted.error         AS Error,
               inserted.retry_count   AS RetryCount
        FROM   {fullTableName} m
        INNER JOIN batch b ON m.id = b.id;";

        _markDispatchedSql = $@"
        DELETE FROM {fullTableName}
        WHERE EXISTS (
            SELECT 1 
            FROM @Keys k
            WHERE k.Id = {fullTableName}.id AND k.CreatedAt = {fullTableName}.created_at
        ) AND owner_id = @OwnerId;";

        _markFailedSql = $@"
        UPDATE {fullTableName}
        SET    state      = @State,
               error      = @Error,
               updated_at = SYSDATETIMEOFFSET(),
               retry_count = retry_count + 1,
               owner_id   = NULL,
               deliver_at  = CASE
                   WHEN @State = 3 THEN DATEADD(second,
                       CASE WHEN retry_count > 11 THEN 3600
                            ELSE POWER(2, retry_count) * 10
                       END,
                       SYSDATETIMEOFFSET())
                   ELSE deliver_at
               END
        WHERE  EXISTS (
            SELECT 1 
            FROM @Keys k
            WHERE k.Id = {fullTableName}.id AND k.CreatedAt = {fullTableName}.created_at
        )
          AND  created_at > DATEADD(DAY, -@MaxAgeDays, SYSDATETIMEOFFSET())
          AND  owner_id = @OwnerId;";

        _reclaimSql = $@"
            UPDATE {fullTableName}
            SET state = 0, updated_at = SYSDATETIMEOFFSET(), owner_id = NULL
            WHERE state = 1
              AND updated_at < DATEADD(SECOND, -@StaleSeconds, SYSDATETIMEOFFSET())
              AND created_at > DATEADD(DAY, -@MaxAgeDays, SYSDATETIMEOFFSET());
            SELECT @@ROWCOUNT;";

        _countSql = $"SELECT COUNT(*) FROM {fullTableName} WITH (NOLOCK) WHERE state IN (0, 3);";

        _purgeDispatchedSql = $@"
            DELETE TOP (@BatchSize) FROM {fullTableName}
            WHERE state = 2
              AND (processed_at < @Cutoff OR (processed_at IS NULL AND updated_at < @Cutoff));";
    }

    /// <inheritdoc/>

    public async ValueTask InsertAsync(OutboxMessage record, EricksonLopez.Outbox.Persistence.IOutboxTransactionContext transaction, CancellationToken cancellationToken = default)
    {

        var conn = transaction.Connection as Microsoft.Data.SqlClient.SqlConnection 
                   ?? throw new InvalidOperationException("Transaction connection is not a SqlConnection.");
        var sqlTx = transaction.Transaction as Microsoft.Data.SqlClient.SqlTransaction;

        using var cmd = new Microsoft.Data.SqlClient.SqlCommand(_insertSql, conn, sqlTx);
        cmd.Parameters.AddWithValue("@Id", record.Id);
        cmd.Parameters.AddWithValue("@MessageType", record.MessageType);
        cmd.Parameters.AddWithValue("@Payload", record.Payload.ToArray());
        cmd.Parameters.AddWithValue("@CorrelationId", (object?)record.CorrelationId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@CausationId", (object?)record.CausationId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@HeadersJson", record.Headers.ToArray());
        cmd.Parameters.AddWithValue("@CreatedAt", record.CreatedAt);
        cmd.Parameters.AddWithValue("@DeliverAt", (object?)record.DeliverAt ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async ValueTask<IReadOnlyList<OutboxMessage>> FetchPendingAsync(int batchSize, CancellationToken cancellationToken = default)
    {
        using var conn = _connectionFactory() as Microsoft.Data.SqlClient.SqlConnection 
                         ?? throw new InvalidOperationException("Connection is not SqlConnection.");
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        using var cmd = new Microsoft.Data.SqlClient.SqlCommand(_fetchPendingSql, conn);
        cmd.Parameters.AddWithValue("@BatchSize", batchSize);
        cmd.Parameters.AddWithValue("@OwnerId", _instanceId);

        var result = new List<OutboxMessage>();
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        
        if (!reader.HasRows) return result;
        
        var idOrd = reader.GetOrdinal("Id");
        var messageTypeOrd = reader.GetOrdinal("MessageType");
        var payloadOrd = reader.GetOrdinal("Payload");
        var correlationIdOrd = reader.GetOrdinal("CorrelationId");
        var causationIdOrd = reader.GetOrdinal("CausationId");
        var headersJsonOrd = reader.GetOrdinal("HeadersJson");
        var createdAtOrd = reader.GetOrdinal("CreatedAt");
        var processedAtOrd = reader.GetOrdinal("ProcessedAt");
        var deliverAtOrd = reader.GetOrdinal("DeliverAt");
        var stateOrd = reader.GetOrdinal("State");
        var errorOrd = reader.GetOrdinal("Error");
        var retryCountOrd = reader.GetOrdinal("RetryCount");

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var state = (OutboxMessageStatus)reader.GetInt32(stateOrd);

            result.Add(new OutboxMessage(
                Id: reader.GetGuid(idOrd),
                MessageType: reader.GetString(messageTypeOrd),
                Payload: reader.IsDBNull(payloadOrd) ? System.Text.Encoding.UTF8.GetBytes("{}") : reader.GetFieldValue<byte[]>(payloadOrd),
                CorrelationId: reader.IsDBNull(correlationIdOrd) ? null : reader.GetString(correlationIdOrd),
                CausationId: reader.IsDBNull(causationIdOrd) ? null : reader.GetString(causationIdOrd),
                Headers: reader.IsDBNull(headersJsonOrd) ? System.Text.Encoding.UTF8.GetBytes("{}") : reader.GetFieldValue<byte[]>(headersJsonOrd),
                CreatedAt: reader.GetDateTimeOffset(createdAtOrd),
                ProcessedAt: reader.IsDBNull(processedAtOrd) ? null : reader.GetDateTimeOffset(processedAtOrd),
                DeliverAt: reader.IsDBNull(deliverAtOrd) ? null : reader.GetDateTimeOffset(deliverAtOrd),
                Status: (EricksonLopez.Outbox.OutboxMessageStatus)state,
                RetryCount: reader.GetInt32(retryCountOrd),
                Error: reader.IsDBNull(errorOrd) ? null : reader.GetString(errorOrd)));
        }
        return result;
    }

    /// <inheritdoc/>

    public async ValueTask InsertBatchAsync(ReadOnlyMemory<OutboxMessage> records, EricksonLopez.Outbox.Persistence.IOutboxTransactionContext transaction, CancellationToken cancellationToken = default)
    {
        if (records.IsEmpty) return;

        var conn = transaction.Connection as Microsoft.Data.SqlClient.SqlConnection 
                   ?? throw new InvalidOperationException("Transaction connection is not a SqlConnection.");
        var sqlTx = transaction.Transaction as Microsoft.Data.SqlClient.SqlTransaction;

        using var bulkCopy = new Microsoft.Data.SqlClient.SqlBulkCopy(conn, Microsoft.Data.SqlClient.SqlBulkCopyOptions.Default, sqlTx);
        bulkCopy.DestinationTableName = _destinationTableName;
        

        // Stryker disable Statement, String
        bulkCopy.ColumnMappings.Add("id", "id");
        bulkCopy.ColumnMappings.Add("type", "type");
        bulkCopy.ColumnMappings.Add("payload", "payload");
        bulkCopy.ColumnMappings.Add("correlation_id", "correlation_id");
        bulkCopy.ColumnMappings.Add("causation_id", "causation_id");
        bulkCopy.ColumnMappings.Add("headers_json", "headers_json");
        bulkCopy.ColumnMappings.Add("state", "state");
        bulkCopy.ColumnMappings.Add("created_at", "created_at");
        bulkCopy.ColumnMappings.Add("updated_at", "updated_at");
        bulkCopy.ColumnMappings.Add("deliver_at", "deliver_at");
        // Stryker restore Statement, String

        using var reader = new OutboxMessageDataReader(records);
        await bulkCopy.WriteToServerAsync(reader, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>

    public async ValueTask MarkAsDispatchedAsync(IReadOnlyList<OutboxMessage> messages, CancellationToken cancellationToken = default)
    {
        var records = CreateKeysRecords(messages).ToList();
        if (records.Count == 0) return;

        using var conn = _connectionFactory() as Microsoft.Data.SqlClient.SqlConnection 
                         ?? throw new InvalidOperationException("Connection is not SqlConnection.");
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        using var cmd = new Microsoft.Data.SqlClient.SqlCommand(_markDispatchedSql, conn);
        cmd.Parameters.AddWithValue("@OwnerId", _instanceId);
        
        var keysParam = cmd.Parameters.AddWithValue("@Keys", records);
        keysParam.SqlDbType = System.Data.SqlDbType.Structured;
        keysParam.TypeName = "[outbox].[MessageKeysType]";

        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>

    public async ValueTask MarkAsFailedAsync(IReadOnlyList<OutboxMessage> messages, string error, bool isDeadLetter = false, CancellationToken cancellationToken = default)
    {
        var records = CreateKeysRecords(messages).ToList();
        if (records.Count == 0) return;

        using var conn = _connectionFactory() as Microsoft.Data.SqlClient.SqlConnection 
                         ?? throw new InvalidOperationException("Connection is not SqlConnection.");
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        using var cmd = new Microsoft.Data.SqlClient.SqlCommand(_markFailedSql, conn);
        cmd.Parameters.AddWithValue("@State", isDeadLetter ? 4 : 3);
        cmd.Parameters.AddWithValue("@Error", (object?)error ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@MaxAgeDays", (int)_options.MaxMessageAge.TotalDays);
        cmd.Parameters.AddWithValue("@OwnerId", _instanceId);
        
        var keysParam = cmd.Parameters.AddWithValue("@Keys", records);
        keysParam.SqlDbType = System.Data.SqlDbType.Structured;
        keysParam.TypeName = "[outbox].[MessageKeysType]";

        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async ValueTask<int> ReclaimStaleMessagesAsync(
        TimeSpan staleTimeout,
        CancellationToken cancellationToken = default)
    {
        // P1-FIX: Added MaxAgeDays guard to prevent re-activating very old messages.
        // Consistent with PostgreSQL (make_interval(days => @MaxAgeDays)),
        // MySQL (DATE_SUB(UTC_TIMESTAMP(), INTERVAL @MaxAgeDays DAY)),
        // and Oracle (SYSTIMESTAMP - NUMTODSINTERVAL(@MaxAgeDays * 86400, 'SECOND')).
        using var conn = _connectionFactory() as Microsoft.Data.SqlClient.SqlConnection 
                         ?? throw new InvalidOperationException("Connection is not SqlConnection.");
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        using var cmd = new Microsoft.Data.SqlClient.SqlCommand(_reclaimSql, conn);
        cmd.Parameters.AddWithValue("@StaleSeconds", (int)staleTimeout.TotalSeconds);
        cmd.Parameters.AddWithValue("@MaxAgeDays", (int)_options.MaxMessageAge.TotalDays);
        
        var result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result != null ? Convert.ToInt32(result, System.Globalization.CultureInfo.InvariantCulture) : 0;
    }

    /// <inheritdoc/>
    public async ValueTask<long> GetPendingCountAsync(CancellationToken cancellationToken = default)
    {
        using var conn = _connectionFactory() as Microsoft.Data.SqlClient.SqlConnection 
                         ?? throw new InvalidOperationException("Connection is not SqlConnection.");
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        using var cmd = new Microsoft.Data.SqlClient.SqlCommand(_countSql, conn);
        var result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result != null ? Convert.ToInt32(result, System.Globalization.CultureInfo.InvariantCulture) : 0;
    }

    /// <inheritdoc/>

    public async ValueTask<int> PurgeDispatchedMessagesAsync(
        DateTimeOffset cutoff,
        int batchSize = 1000,
        CancellationToken cancellationToken = default)
    {
        if (batchSize <= 0) return 0;

        using var conn = _connectionFactory() as Microsoft.Data.SqlClient.SqlConnection 
                         ?? throw new InvalidOperationException("Connection is not SqlConnection.");
        if (conn.State != ConnectionState.Open)
        {
            await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        }

        using var cmd = new Microsoft.Data.SqlClient.SqlCommand(_purgeDispatchedSql, conn);
        cmd.Parameters.AddWithValue("@Cutoff", cutoff);
        cmd.Parameters.AddWithValue("@BatchSize", batchSize);

        return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static IEnumerable<Microsoft.Data.SqlClient.Server.SqlDataRecord> CreateKeysRecords(IReadOnlyList<OutboxMessage> messages)
    {
        var metaData = new Microsoft.Data.SqlClient.Server.SqlMetaData[]
        {
            new("Id", System.Data.SqlDbType.UniqueIdentifier),
            new("CreatedAt", System.Data.SqlDbType.DateTimeOffset)
        };

        var record = new Microsoft.Data.SqlClient.Server.SqlDataRecord(metaData);
        for (int i = 0; i < messages.Count; i++)
        {
            var message = messages[i];
            record.SetGuid(0, message.Id);
            record.SetDateTimeOffset(1, message.CreatedAt);
            yield return record;
        }
}
}










