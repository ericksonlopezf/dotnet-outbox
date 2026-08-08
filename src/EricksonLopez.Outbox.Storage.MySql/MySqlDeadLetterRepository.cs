using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using MySqlConnector;
using EricksonLopez.Outbox;

using EricksonLopez.Outbox.Persistence;

namespace EricksonLopez.Outbox.Storage.MySql;

/// <summary>
/// MySQL implementation of <see cref="IDeadLetterRepository"/>.
/// </summary>
public sealed class MySqlDeadLetterRepository : IDeadLetterRepository
{
    private readonly Func<IDbConnection> _connectionFactory;
    private readonly string _insertSql;
    private readonly string _getSql;
    private readonly string _deleteSql;
    private readonly string _purgeSql;

    /// <inheritdoc/>
    public bool IsFirstPartyImplementation => true;

    /// <summary>
    /// Initializes a new instance of the <see cref="MySqlDeadLetterRepository"/> class.
    /// </summary>
    /// <param name="connectionFactory">The factory that creates MySQL connections.</param>
    /// <param name="options">The outbox runtime options.</param>
    /// <exception cref="ArgumentNullException"><paramref name="connectionFactory"/> or <paramref name="options"/> is <see langword="null"/>.</exception>
    public MySqlDeadLetterRepository(Func<IDbConnection> connectionFactory, IOptionsMonitor<OutboxRuntimeOptions> options)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        ArgumentNullException.ThrowIfNull(options);

        var table = options.CurrentValue.TableName + "_dead_letters";
        var fullTableName = string.IsNullOrWhiteSpace(options.CurrentValue.SchemaName) 
            ? $"`{table}`" 
            : $"`{options.CurrentValue.SchemaName}`.`{table}`";

        _insertSql = $@"
            INSERT IGNORE INTO {fullTableName} (id, original_message_id, type, payload, correlation_id, causation_id, headers_json, created_at, dead_lettered_at, retry_count, reason, last_error)
            VALUES (@Id, @OriginalMessageId, @Type, @Payload, @CorrelationId, @CausationId, @HeadersJson, @CreatedAt, @DeadLetteredAt, @RetryCount, @ErrorReason, @LastError);";

        _getSql = $@"
            SELECT id, original_message_id, type, payload, correlation_id, causation_id, headers_json, created_at, dead_lettered_at, retry_count, reason, last_error
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
        MySqlConnection? conn = null;
        MySqlTransaction? tx = null;
        bool disposeConn = false;

        if (transaction != null)
        {
            conn = (transaction.Connection as MySqlConnection);
            tx = (transaction.Transaction as MySqlTransaction);
        }
        else
        {
            conn = (MySqlConnection)_connectionFactory();
            disposeConn = true;
            await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        }

        try
        {
            using var cmd = new MySqlCommand(_insertSql, conn, tx);
            cmd.Parameters.AddWithValue("@Id", message.Id);
            cmd.Parameters.AddWithValue("@OriginalMessageId", message.OriginalMessageId);
            cmd.Parameters.AddWithValue("@Type", message.MessageType);
            cmd.Parameters.AddWithValue("@Payload", System.Text.Encoding.UTF8.GetString(message.Payload.Span));
            cmd.Parameters.AddWithValue("@CorrelationId", (object?)message.CorrelationId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@CausationId", (object?)message.CausationId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@HeadersJson", System.Text.Encoding.UTF8.GetString(message.Headers.Span));
            cmd.Parameters.AddWithValue("@CreatedAt", message.CreatedAt.UtcDateTime);
            cmd.Parameters.AddWithValue("@DeadLetteredAt", message.DeadLetteredAt.UtcDateTime);
            cmd.Parameters.AddWithValue("@RetryCount", message.RetryCount);
            cmd.Parameters.AddWithValue("@ErrorReason", message.Reason ?? "Unknown");
            cmd.Parameters.AddWithValue("@LastError", (object?)message.LastError ?? DBNull.Value);

            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (disposeConn && conn != null)
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
        using var conn = (MySqlConnection)_connectionFactory();
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        using var cmd = new MySqlCommand(_getSql, conn);
        cmd.Parameters.AddWithValue("@Limit", limit);
        cmd.Parameters.AddWithValue("@After", after.HasValue ? (object)after.Value.UtcDateTime : DBNull.Value);

        var results = new List<DeadLetterMessage>();
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(new DeadLetterMessage(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? "{}"u8.ToArray() : reader.GetFieldValue<byte[]>(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.IsDBNull(6) ? "{}"u8.ToArray() : reader.GetFieldValue<byte[]>(6),
                reader.GetFieldValue<DateTimeOffset>(7),
                reader.GetFieldValue<DateTimeOffset>(8),
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
        using var conn = (MySqlConnection)_connectionFactory();
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        using var cmd = new MySqlCommand(_deleteSql, conn);
        cmd.Parameters.AddWithValue("@Id", id);
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async ValueTask PurgeAsync(DateTimeOffset olderThan, CancellationToken cancellationToken = default)
    {
        using var conn = (MySqlConnection)_connectionFactory();
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        using var cmd = new MySqlCommand(_purgeSql, conn);
        cmd.Parameters.AddWithValue("@OlderThan", olderThan.UtcDateTime);
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
