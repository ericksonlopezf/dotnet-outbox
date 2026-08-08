using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Npgsql;
using EricksonLopez.Outbox;

using EricksonLopez.Outbox.Persistence;

namespace EricksonLopez.Outbox.Storage.PostgreSql;

/// <summary>
/// PostgreSQL implementation of the Dead Letter Queue storage.
/// </summary>
public sealed class PostgreSqlDeadLetterRepository : IDeadLetterRepository
{
    private readonly NpgsqlDataSource _dataSource;

    private readonly string _insertSql;
    private readonly string _getSql;
    private readonly string _deleteSql;
    private readonly string _purgeSql;

    /// <inheritdoc/>
    public bool IsFirstPartyImplementation => true;

    /// <summary>
    /// Initializes a new instance of the <see cref="PostgreSqlDeadLetterRepository"/> class.
    /// </summary>
    /// <param name="dataSource">The PostgreSQL data source.</param>
    /// <param name="options">The outbox runtime options.</param>
    /// <exception cref="ArgumentNullException"><paramref name="dataSource"/> or <paramref name="options"/> is <see langword="null"/>.</exception>
    [CLSCompliant(false)]
    public PostgreSqlDeadLetterRepository(NpgsqlDataSource dataSource, IOptionsMonitor<OutboxRuntimeOptions> options)
    {
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
        ArgumentNullException.ThrowIfNull(options);

        var schema = string.IsNullOrWhiteSpace(options.CurrentValue.SchemaName) ? "public" : options.CurrentValue.SchemaName;
        var table = options.CurrentValue.TableName + "_dead_letters";
        var fullTableName = $"\"{schema}\".\"{table}\"";

        _insertSql = $@"
            INSERT INTO {fullTableName} (id, original_message_id, type, payload, correlation_id, causation_id, headers_json, created_at, dead_lettered_at, retry_count, error_reason, last_error)
            VALUES (@Id, @OriginalMessageId, @Type, @Payload, @CorrelationId, @CausationId, @HeadersJson, @CreatedAt, @DeadLetteredAt, @RetryCount, @ErrorReason, @LastError)
            ON CONFLICT DO NOTHING;";

        _getSql = $@"
            SELECT id, original_message_id, type, payload, correlation_id, causation_id, headers_json, created_at, dead_lettered_at, retry_count, error_reason, last_error
            FROM {fullTableName}
            WHERE (@After IS NULL OR dead_lettered_at > @After)
            ORDER BY dead_lettered_at ASC
            LIMIT @Limit;";

        _deleteSql = $"DELETE FROM {fullTableName} WHERE id = @Id;";
        _purgeSql = $"DELETE FROM {fullTableName} WHERE dead_lettered_at < @OlderThan;";
    }

    /// <inheritdoc/>
    public async ValueTask InsertAsync(
        DeadLetterMessage message,
        IOutboxTransactionContext? transaction = default,
        CancellationToken cancellationToken = default)
    {
        // If a transaction is provided, use its connection. Otherwise, rent from the pool.
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
            
            var payloadArray = System.Runtime.InteropServices.MemoryMarshal.TryGetArray(message.Payload, out var payloadSeg) && payloadSeg.Offset == 0 && payloadSeg.Count == payloadSeg.Array!.Length 
                ? payloadSeg.Array 
                : message.Payload.ToArray();

            cmd.Parameters.Add(new NpgsqlParameter("Id", NpgsqlTypes.NpgsqlDbType.Uuid) { Value = message.Id });
            cmd.Parameters.Add(new NpgsqlParameter("OriginalMessageId", NpgsqlTypes.NpgsqlDbType.Uuid) { Value = message.OriginalMessageId });
            cmd.Parameters.Add(new NpgsqlParameter("Type", NpgsqlTypes.NpgsqlDbType.Varchar) { Value = message.MessageType });
            cmd.Parameters.Add(new NpgsqlParameter("Payload", NpgsqlTypes.NpgsqlDbType.Jsonb) { Value = payloadArray });
            cmd.Parameters.Add(new NpgsqlParameter("CorrelationId", NpgsqlTypes.NpgsqlDbType.Varchar) { Value = (object?)message.CorrelationId ?? DBNull.Value });
            cmd.Parameters.Add(new NpgsqlParameter("CausationId", NpgsqlTypes.NpgsqlDbType.Varchar) { Value = (object?)message.CausationId ?? DBNull.Value });
            
            var headersArray = System.Runtime.InteropServices.MemoryMarshal.TryGetArray(message.Headers, out var headersSeg) && headersSeg.Offset == 0 && headersSeg.Count == headersSeg.Array!.Length 
                ? headersSeg.Array 
                : message.Headers.ToArray();
            cmd.Parameters.Add(new NpgsqlParameter("HeadersJson", NpgsqlTypes.NpgsqlDbType.Jsonb) { Value = headersArray });
            
            cmd.Parameters.Add(new NpgsqlParameter("CreatedAt", NpgsqlTypes.NpgsqlDbType.TimestampTz) { Value = message.CreatedAt });
            cmd.Parameters.Add(new NpgsqlParameter("DeadLetteredAt", NpgsqlTypes.NpgsqlDbType.TimestampTz) { Value = message.DeadLetteredAt });
            cmd.Parameters.Add(new NpgsqlParameter("RetryCount", NpgsqlTypes.NpgsqlDbType.Integer) { Value = message.RetryCount });
            cmd.Parameters.Add(new NpgsqlParameter("ErrorReason", NpgsqlTypes.NpgsqlDbType.Text) { Value = message.Reason ?? "Unknown" });
            cmd.Parameters.Add(new NpgsqlParameter("LastError", NpgsqlTypes.NpgsqlDbType.Text) { Value = (object?)message.LastError ?? DBNull.Value });

            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
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
    public async ValueTask<IReadOnlyList<DeadLetterMessage>> GetAsync(
        int limit = 100,
        DateTimeOffset? after = null,
        CancellationToken cancellationToken = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand(_getSql, conn);
        cmd.Parameters.Add(new NpgsqlParameter("After", NpgsqlTypes.NpgsqlDbType.TimestampTz) { Value = (object?)after ?? DBNull.Value });
        cmd.Parameters.Add(new NpgsqlParameter("Limit", NpgsqlTypes.NpgsqlDbType.Integer) { Value = limit });

        var results = new List<DeadLetterMessage>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var id = reader.GetGuid(0);
            var originalMessageId = reader.GetGuid(1);
            var type = reader.GetString(2);
            // Read JSONB columns directly as byte[] — avoids the UTF-8→UTF-16→UTF-8 round-trip
            // that would occur if using reader.GetString() + Encoding.UTF8.GetBytes().
            var payloadBytes = reader.IsDBNull(3) ? "{}".Select(c => (byte)c).ToArray() : reader.GetFieldValue<byte[]>(3);
            var correlationId = reader.IsDBNull(4) ? null : reader.GetString(4);
            var causationId = reader.IsDBNull(5) ? null : reader.GetString(5);
            var headersBytes = reader.IsDBNull(6) ? "{}".Select(c => (byte)c).ToArray() : reader.GetFieldValue<byte[]>(6);
            var createdAt = reader.GetFieldValue<DateTimeOffset>(7);
            var deadLetteredAt = reader.GetFieldValue<DateTimeOffset>(8);
            var retryCount = reader.GetInt32(9);
            var errorReason = reader.GetString(10);
            var lastError = reader.IsDBNull(11) ? null : reader.GetString(11);

            results.Add(new DeadLetterMessage(
                id,
                originalMessageId,
                type,
                payloadBytes,
                correlationId,
                causationId,
                headersBytes,
                createdAt,
                deadLetteredAt,
                retryCount,
                errorReason,
                lastError
            ));
        }
        return results;
    }

    /// <inheritdoc/>
    public async ValueTask DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand(_deleteSql, conn);
        cmd.Parameters.Add(new NpgsqlParameter("Id", NpgsqlTypes.NpgsqlDbType.Uuid) { Value = id });
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async ValueTask PurgeAsync(DateTimeOffset olderThan, CancellationToken cancellationToken = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand(_purgeSql, conn);
        cmd.Parameters.Add(new NpgsqlParameter("OlderThan", NpgsqlTypes.NpgsqlDbType.TimestampTz) { Value = olderThan });
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}





