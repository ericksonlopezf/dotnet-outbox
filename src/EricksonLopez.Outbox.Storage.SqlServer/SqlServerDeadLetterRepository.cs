using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

using EricksonLopez.Outbox.Persistence;
using Microsoft.Extensions.Options;

namespace EricksonLopez.Outbox.Storage.SqlServer;

/// <summary>
/// SQL Server implementation of the Dead Letter Queue storage.
/// </summary>
public sealed class SqlServerDeadLetterRepository : IDeadLetterRepository
{
    private readonly Func<IDbConnection> _connectionFactory;
    private readonly string _insertSql;
    private readonly string _getSql;
    private readonly string _deleteSql;
    private readonly string _purgeSql;

    /// <inheritdoc/>
    public bool IsFirstPartyImplementation => true;

    /// <summary>
    /// Initializes a new instance of the <see cref="SqlServerDeadLetterRepository"/> class.
    /// </summary>
    /// <param name="connectionFactory">The factory that creates SQL Server connections.</param>
    /// <param name="options">The outbox runtime options.</param>
    /// <exception cref="ArgumentNullException"><paramref name="connectionFactory"/> or <paramref name="options"/> is <see langword="null"/>.</exception>
    public SqlServerDeadLetterRepository(Func<IDbConnection> connectionFactory, IOptionsMonitor<OutboxRuntimeOptions> options)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        ArgumentNullException.ThrowIfNull(options);

        var schema = string.IsNullOrWhiteSpace(options.CurrentValue.SchemaName) ? "dbo" : options.CurrentValue.SchemaName;
        var table = options.CurrentValue.TableName + "_dead_letters";
        var fullTableName = $"[{schema}].[{table}]";

        _insertSql = $@"
            IF NOT EXISTS (SELECT 1 FROM {fullTableName} WITH (UPDLOCK, HOLDLOCK) WHERE id = @Id)
            BEGIN
                INSERT INTO {fullTableName} (id, original_message_id, type, payload, correlation_id, causation_id, headers_json, created_at, retry_count, reason, last_error)
                VALUES (@Id, @OriginalMessageId, @Type, @Payload, @CorrelationId, @CausationId, @HeadersJson, @CreatedAt, @RetryCount, @ErrorReason, @LastError)
            END;";

        _getSql = $@"
            SELECT TOP (@Limit) id, original_message_id, type, payload, correlation_id, causation_id, headers_json, created_at, dead_lettered_at, retry_count, reason, last_error
            FROM {fullTableName}
            WHERE (@After IS NULL OR dead_lettered_at > @After)
            ORDER BY dead_lettered_at ASC;";

        _deleteSql = $"DELETE FROM {fullTableName} WHERE id = @Id;";
        _purgeSql = $"DELETE FROM {fullTableName} WHERE dead_lettered_at < @OlderThan;";
    }

    /// <inheritdoc/>
    public async ValueTask InsertAsync(
        DeadLetterMessage message,
        IOutboxTransactionContext? transaction = default,
        CancellationToken cancellationToken = default)
    {
        Microsoft.Data.SqlClient.SqlConnection? conn = null;
        Microsoft.Data.SqlClient.SqlTransaction? tx = null;
        bool closeConn = false;

        if (transaction != null)
        {
            conn = transaction.Connection as Microsoft.Data.SqlClient.SqlConnection ?? throw new System.InvalidOperationException("Connection must be a SqlConnection");
            tx = transaction.Transaction as Microsoft.Data.SqlClient.SqlTransaction;
        }
        else
        {
            conn = (Microsoft.Data.SqlClient.SqlConnection)_connectionFactory();
            closeConn = true;
            await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        }

        try
        {
            using var cmd = new Microsoft.Data.SqlClient.SqlCommand(_insertSql, conn, tx);
            cmd.Parameters.AddWithValue("@Id", message.Id);
            cmd.Parameters.AddWithValue("@OriginalMessageId", message.OriginalMessageId);
            cmd.Parameters.AddWithValue("@Type", message.MessageType);
            cmd.Parameters.AddWithValue("@Payload", System.Text.Encoding.UTF8.GetString(message.Payload.Span));
            cmd.Parameters.AddWithValue("@CorrelationId", (object?)message.CorrelationId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@CausationId", (object?)message.CausationId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@HeadersJson", System.Text.Encoding.UTF8.GetString(message.Headers.Span));
            cmd.Parameters.AddWithValue("@CreatedAt", message.CreatedAt);
            cmd.Parameters.AddWithValue("@RetryCount", message.RetryCount);
            cmd.Parameters.AddWithValue("@ErrorReason", message.Reason ?? "Unknown");
            cmd.Parameters.AddWithValue("@LastError", (object?)message.LastError ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (closeConn && conn != null)
            {
                conn.Dispose();
            }
        }
    }

    /// <inheritdoc/>
    public async ValueTask<IReadOnlyList<DeadLetterMessage>> GetAsync(
        int limit = 100,
        DateTimeOffset? after = null,
        CancellationToken cancellationToken = default)
    {
        using var conn = (Microsoft.Data.SqlClient.SqlConnection)_connectionFactory();
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        using var cmd = new Microsoft.Data.SqlClient.SqlCommand(_getSql, conn);
        cmd.Parameters.AddWithValue("@Limit", limit);
        cmd.Parameters.AddWithValue("@After", (object?)after ?? DBNull.Value);

        var results = new List<DeadLetterMessage>();
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(new DeadLetterMessage(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetString(2),
                System.Text.Encoding.UTF8.GetBytes(reader.IsDBNull(3) ? "{}" : reader.GetString(3)),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                System.Text.Encoding.UTF8.GetBytes(reader.IsDBNull(6) ? "{}" : reader.GetString(6)),
                reader.GetDateTimeOffset(7),
                reader.GetDateTimeOffset(8),
                reader.GetInt32(9),
                reader.IsDBNull(10) ? "Unknown" : reader.GetString(10),
                reader.IsDBNull(11) ? null : reader.GetString(11)
            ));
        }

        return results;
    }

    /// <inheritdoc/>
    public async ValueTask DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var conn = (Microsoft.Data.SqlClient.SqlConnection)_connectionFactory();
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        using var cmd = new Microsoft.Data.SqlClient.SqlCommand(_deleteSql, conn);
        cmd.Parameters.AddWithValue("@Id", id);
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async ValueTask PurgeAsync(DateTimeOffset olderThan, CancellationToken cancellationToken = default)
    {
        using var conn = (Microsoft.Data.SqlClient.SqlConnection)_connectionFactory();
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        using var cmd = new Microsoft.Data.SqlClient.SqlCommand(_purgeSql, conn);
        cmd.Parameters.AddWithValue("@OlderThan", olderThan);
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
