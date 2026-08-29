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
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace EricksonLopez.Outbox.Storage.Sqlite;

/// <summary>
/// Provides an SQLite-specific implementation of <see cref="IOutboxRepository"/>.
/// </summary>
public sealed class SqliteOutboxRepository : IOutboxRepository
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
    private readonly string _fullTableName;

    /// <summary>
    /// Initializes a new instance of the <see cref="SqliteOutboxRepository"/> class.
    /// </summary>
    /// <param name="connectionFactory">The factory that creates SQLite connections.</param>
    /// <param name="options">The runtime options containing thresholds and configurations.</param>
    /// <exception cref="ArgumentNullException"><paramref name="connectionFactory"/> is <see langword="null"/>.</exception>

    public SqliteOutboxRepository(Func<IDbConnection> connectionFactory, IOptions<OutboxRuntimeOptions>? options = null)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _options = options?.Value ?? new OutboxRuntimeOptions();
        
        var table = _options.TableName;
        
        if (!System.Text.RegularExpressions.Regex.IsMatch(table, "^[a-zA-Z0-9_]+$", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(1)))
            throw new ArgumentException("Table name contains invalid characters.", nameof(options));

        _fullTableName = table == "outbox_messages" ? "outbox_messages" : $"\"{table}\"";

        _insertSql = $@"
        INSERT INTO {_fullTableName} 
            (id, type, payload, correlation_id, causation_id, headers_json, state, created_at, updated_at, deliver_at, retry_count)
        VALUES 
            (@Id, @MessageType, @Payload, @CorrelationId, @CausationId, @HeadersJson, 0, @CreatedAt, @CreatedAt, @DeliverAt, 0)
        ON CONFLICT (id) DO NOTHING;";

        _fetchPendingSql = $@"
        WITH batch AS (
            SELECT id
            FROM {_fullTableName}
            WHERE state IN (0, 3)
              AND (deliver_at IS NULL OR deliver_at <= @Now)
            ORDER BY created_at ASC, id ASC
            LIMIT @BatchSize
        )
        UPDATE {_fullTableName}
        SET state = 1, updated_at = @Now
        WHERE id IN batch
        RETURNING id AS Id, type AS MessageType, payload AS Payload, correlation_id AS CorrelationId,
                  causation_id AS CausationId, headers_json AS HeadersJson, created_at AS CreatedAt, 
                  processed_at AS ProcessedAt, deliver_at AS DeliverAt, state AS State, retry_count AS RetryCount, error AS Error;";

        _markDispatchedSql = $@"
        DELETE FROM {_fullTableName}
        WHERE id IN @Ids;";

        _markFailedSql = $@"
        UPDATE {_fullTableName}
        SET state = @State, 
            error = @Error, 
            updated_at = datetime('now'), 
            retry_count = retry_count + 1,
            deliver_at = CASE 
                WHEN @State = 3 THEN datetime('now', '+' || (min(1 << retry_count, 3600)) * 10 || ' seconds')
                ELSE deliver_at 
            END
        WHERE id IN @Ids;";

        _reclaimSql = $@"
            UPDATE {_fullTableName}
            SET state = 0, updated_at = @Now
            WHERE state = 1
              AND updated_at <= @StaleTime
              AND created_at > @MaxAge;";

        _countSql = $"SELECT COUNT(*) FROM {_fullTableName} WHERE state IN (0, 3);";

        _purgeDispatchedSql = $@"
            DELETE FROM {_fullTableName}
            WHERE id IN (
                SELECT id FROM {_fullTableName}
                WHERE state = 2
                  AND (processed_at < @Cutoff OR (processed_at IS NULL AND updated_at < @Cutoff))
                ORDER BY created_at ASC
                LIMIT @BatchSize
            );";
    }

    /// <inheritdoc/>

    public async ValueTask InsertAsync(OutboxMessage record, IOutboxTransactionContext transaction, CancellationToken cancellationToken = default)
    {
        var conn = (transaction.Connection as System.Data.Common.DbConnection) ?? throw new InvalidOperationException("Transaction connection is null.");
        using var cmd = conn.CreateCommand();
        cmd.Transaction = (transaction.Transaction as System.Data.Common.DbTransaction);
        cmd.CommandText = _insertSql;
        cmd.Parameters.Add(new SqliteParameter("@Id", record.Id.ToString()));
        cmd.Parameters.Add(new SqliteParameter("@MessageType", record.MessageType));
        cmd.Parameters.Add(new SqliteParameter("@Payload", record.Payload.ToArray()));
        cmd.Parameters.Add(new SqliteParameter("@CorrelationId", record.CorrelationId ?? (object)DBNull.Value));
        cmd.Parameters.Add(new SqliteParameter("@CausationId", record.CausationId ?? (object)DBNull.Value));
        cmd.Parameters.Add(new SqliteParameter("@HeadersJson", record.Headers.ToArray()));
        cmd.Parameters.Add(new SqliteParameter("@CreatedAt", record.CreatedAt.UtcDateTime.ToString("O")));
        cmd.Parameters.Add(new SqliteParameter("@DeliverAt", record.DeliverAt?.UtcDateTime.ToString("O") ?? (object)DBNull.Value));
        
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>

    public async ValueTask InsertBatchAsync(ReadOnlyMemory<OutboxMessage> records, IOutboxTransactionContext transaction, CancellationToken cancellationToken = default)
    {
        if (records.IsEmpty) return;
        var conn = (transaction.Connection as System.Data.Common.DbConnection) ?? throw new InvalidOperationException("Transaction connection is null.");
        
        var span = records.Span;
        using var cmd = conn.CreateCommand();
        cmd.Transaction = (transaction.Transaction as System.Data.Common.DbTransaction);
        
        var sb = new System.Text.StringBuilder();
        sb.Append("INSERT INTO ").Append(_fullTableName).Append(" (id, type, payload, correlation_id, causation_id, headers_json, state, created_at, updated_at, deliver_at, retry_count) VALUES ");

        for (int i = 0; i < span.Length; i++)
        {
            var r = span[i];
            sb.AppendFormat(System.Globalization.CultureInfo.InvariantCulture, "(@Id{0}, @Type{0}, @Payload{0}, @CorrelationId{0}, @CausationId{0}, @HeadersJson{0}, 0, @CreatedAt{0}, @CreatedAt{0}, @DeliverAt{0}, 0)", i);
            if (i < span.Length - 1) sb.Append(", ");
            
            cmd.Parameters.Add(new SqliteParameter($"@Id{i}", r.Id.ToString()));
            cmd.Parameters.Add(new SqliteParameter($"@Type{i}", r.MessageType));
            cmd.Parameters.Add(new SqliteParameter($"@Payload{i}", r.Payload.ToArray()));
            cmd.Parameters.Add(new SqliteParameter($"@CorrelationId{i}", r.CorrelationId ?? (object)DBNull.Value));
            cmd.Parameters.Add(new SqliteParameter($"@CausationId{i}", r.CausationId ?? (object)DBNull.Value));
            cmd.Parameters.Add(new SqliteParameter($"@HeadersJson{i}", r.Headers.ToArray()));
            cmd.Parameters.Add(new SqliteParameter($"@CreatedAt{i}", r.CreatedAt.UtcDateTime.ToString("O")));
            cmd.Parameters.Add(new SqliteParameter($"@DeliverAt{i}", r.DeliverAt?.UtcDateTime.ToString("O") ?? (object)DBNull.Value));
        }
        
        sb.Append(" ON CONFLICT (id) DO NOTHING;");
        cmd.CommandText = sb.ToString();
        
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async ValueTask<IReadOnlyList<OutboxMessage>> FetchPendingAsync(int batchSize, CancellationToken cancellationToken = default)
    {
        using var conn = (System.Data.Common.DbConnection)_connectionFactory();
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        
        using var cmd = conn.CreateCommand();
        cmd.CommandText = _fetchPendingSql;
        cmd.Parameters.Add(new SqliteParameter("@BatchSize", batchSize));
        cmd.Parameters.Add(new SqliteParameter("@Now", DateTimeOffset.UtcNow.ToString("O")));
        
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var result = new List<OutboxMessage>(batchSize);
        
        if (!reader.HasRows) return result;
        
        var idOrd = reader.GetOrdinal("Id");
        var messageTypeOrd = reader.GetOrdinal("MessageType");
        var payloadOrd = reader.GetOrdinal("Payload");
        var correlationIdOrd = reader.GetOrdinal("CorrelationId");
        var causationIdOrd = reader.GetOrdinal("CausationId");
        var headersOrd = reader.GetOrdinal("HeadersJson");
        var createdAtOrd = reader.GetOrdinal("CreatedAt");
        var processedAtOrd = reader.GetOrdinal("ProcessedAt");
        var deliverAtOrd = reader.GetOrdinal("DeliverAt");
        var stateOrd = reader.GetOrdinal("State");
        var errorOrd = reader.GetOrdinal("Error");
        var retryCountOrd = reader.GetOrdinal("RetryCount");

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var state = (OutboxMessageStatus)reader.GetInt64(stateOrd);

            var processedAtString = reader.IsDBNull(processedAtOrd) ? null : reader.GetString(processedAtOrd);
            var deliverAtString = reader.IsDBNull(deliverAtOrd) ? null : reader.GetString(deliverAtOrd);
            result.Add(new OutboxMessage(
                Id: Guid.Parse(reader.GetString(idOrd)),
                MessageType: reader.GetString(messageTypeOrd),
                Payload: reader.GetFieldValue<byte[]>(payloadOrd),
                CorrelationId: reader.IsDBNull(correlationIdOrd) ? null : reader.GetString(correlationIdOrd),
                CausationId: reader.IsDBNull(causationIdOrd) ? null : reader.GetString(causationIdOrd),
                Headers: reader.GetFieldValue<byte[]>(headersOrd),
                CreatedAt: DateTimeOffset.Parse(reader.GetString(createdAtOrd), null, System.Globalization.DateTimeStyles.RoundtripKind),
                ProcessedAt: string.IsNullOrEmpty(processedAtString) ? null : DateTimeOffset.Parse(processedAtString, null, System.Globalization.DateTimeStyles.RoundtripKind),
                DeliverAt: string.IsNullOrEmpty(deliverAtString) ? null : DateTimeOffset.Parse(deliverAtString, null, System.Globalization.DateTimeStyles.RoundtripKind),
                Status: (EricksonLopez.Outbox.OutboxMessageStatus)state,
                RetryCount: (int)reader.GetInt64(retryCountOrd),
                Error: reader.IsDBNull(errorOrd) ? null : reader.GetString(errorOrd)));
        }
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
        cmd.Parameters.Add(new SqliteParameter("@State", isDeadLetter ? 4 : 3));
        cmd.Parameters.Add(new SqliteParameter("@Error", error ?? (object)DBNull.Value));
        
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async ValueTask<int> ReclaimStaleMessagesAsync(TimeSpan staleTimeout, CancellationToken cancellationToken = default)
    {
        using var conn = (System.Data.Common.DbConnection)_connectionFactory();
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        var now = DateTimeOffset.UtcNow;
        
        using var cmd = conn.CreateCommand();
        cmd.CommandText = _reclaimSql;
        cmd.Parameters.Add(new SqliteParameter("@Now", now.ToString("O")));
        cmd.Parameters.Add(new SqliteParameter("@StaleTime", now.Subtract(staleTimeout).ToString("O")));
        cmd.Parameters.Add(new SqliteParameter("@MaxAge", now.Subtract(_options.MaxMessageAge).ToString("O")));
        
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
        if (batchSize <= 0) return 0;

        using var conn = (System.Data.Common.DbConnection)_connectionFactory();
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);

        using var cmd = conn.CreateCommand();
        cmd.CommandText = _purgeDispatchedSql;

        var pCutoff = cmd.CreateParameter();
        pCutoff.ParameterName = "@Cutoff";
        pCutoff.Value = cutoff.UtcDateTime.ToString("O");
        cmd.Parameters.Add(pCutoff);

        var pBatchSize = cmd.CreateParameter();
        pBatchSize.ParameterName = "@BatchSize";
        pBatchSize.Value = batchSize;
        cmd.Parameters.Add(pBatchSize);

        return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}







