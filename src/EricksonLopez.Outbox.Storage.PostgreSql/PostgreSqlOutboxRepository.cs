// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.Outbox;
using EricksonLopez.Outbox.Persistence;
using EricksonLopez.Result;
using Microsoft.Extensions.Options;
using Npgsql;

namespace EricksonLopez.Outbox.Storage.PostgreSql;

/// <summary>
/// Provides a PostgreSQL-specific implementation of <see cref="IOutboxRepository"/>.
/// Exploits PostgreSQL-exclusive optimizations:
///
///   - <c>FOR UPDATE SKIP LOCKED</c>: Enables concurrent, lock-free polling.
///   - Delete-on-Dispatch: Eradicates MVCC bloat by physically deleting dispatched messages.
///   - UNNEST batch insert: Transactional bulk ingestion inside the caller's transaction.
///   - Binary COPY: Available via InsertBulkAsync for non-transactional bulk imports only.
///   - Native JSONB: Injects UTF-8 payloads directly into PostgreSQL without UTF-16 conversions.
/// </summary>
public sealed partial class PostgreSqlOutboxRepository : IOutboxRepository
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly OutboxRuntimeOptions _options;

    private readonly string _insertSql;
    private readonly string _insertBatchSql;
    private readonly string _fetchPendingSql;
    private readonly string _markDispatchedSql;
    private readonly string _markFailedSql;
    private readonly string _reclaimSql;
    private readonly string _pendingCountSql;
    private readonly string _insertBulkSql;
    private readonly string _purgeDispatchedSql;
    private readonly Guid _instanceId;

    /// <summary>
    /// Initializes a new instance of the <see cref="PostgreSqlOutboxRepository"/> class.
    /// </summary>
    /// <param name="dataSource">The PostgreSQL data source.</param>
    /// <param name="options">The runtime options containing thresholds and configurations.</param>
    /// <exception cref="ArgumentNullException"><paramref name="dataSource"/> is <see langword="null"/>.</exception>
    [CLSCompliant(false)]

    public PostgreSqlOutboxRepository(NpgsqlDataSource dataSource, IOptions<OutboxRuntimeOptions>? options = null)
    {
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
        _options = options?.Value ?? new OutboxRuntimeOptions();

        var schema = _options.SchemaName;
        var table = _options.TableName;
        
        if (!SchemaNameRegex().IsMatch(schema))
            throw new ArgumentException("Schema name contains invalid characters.", nameof(options));
        if (!SchemaNameRegex().IsMatch(table))
            throw new ArgumentException("Table name contains invalid characters.", nameof(options));

        var fullTableName = $@"""{schema}"".""{table}""";
        _instanceId = Guid.Parse(_options.InstanceId);

        _insertSql = $@"
            INSERT INTO {fullTableName}
                (id, type, payload, correlation_id, causation_id, headers_json, deliver_at, state, created_at, updated_at, retry_count)
            VALUES
                (@Id, @MessageType, @Payload, @CorrelationId, @CausationId, @HeadersJson, @DeliverAt, 0, @CreatedAt, @CreatedAt, 0)
            ON CONFLICT DO NOTHING;";

        _insertBatchSql = $@"
            INSERT INTO {fullTableName}
                (id, type, payload, correlation_id, causation_id, headers_json, state, created_at, updated_at, deliver_at, retry_count)
            SELECT
                unnest(@Ids::uuid[]),
                unnest(@Types::varchar[]),
                unnest(@Payloads::jsonb[]),
                unnest(@CorrelationIds::varchar[]),
                unnest(@CausationIds::varchar[]),
                unnest(@Headers::jsonb[]),
                0,
                unnest(@CreatedAts::timestamptz[]),
                unnest(@CreatedAts::timestamptz[]),
                unnest(@DeliverAts::timestamptz[]),
                0
            ON CONFLICT DO NOTHING;";

        _fetchPendingSql = $@"
            WITH batch AS (
                SELECT id, created_at
                FROM   {fullTableName}
                WHERE  (state = 0 OR state = 3)
                  AND  (deliver_at IS NULL OR deliver_at <= NOW())
                  AND  created_at >= NOW() - make_interval(days => @MaxAgeDays)
                ORDER  BY created_at ASC, id ASC
                LIMIT  @BatchSize
                FOR    UPDATE SKIP LOCKED
            )
            UPDATE {fullTableName} m
            SET    state = 1, updated_at = NOW(), owner_id = @OwnerId
            FROM   batch
            WHERE  m.id = batch.id AND m.created_at = batch.created_at
            RETURNING m.id, m.type, m.payload, m.correlation_id,
                      m.causation_id, m.headers_json, m.created_at, m.processed_at,
                      m.deliver_at, m.state, m.error, m.retry_count;";

        // P2-6 AUDIT FIX: Support both DELETE (default, MVCC-optimal) and soft-delete (state=2)
        // for post-mortem debugging and compliance audit trails.
        // SQL is selected once at construction time â€” zero runtime branch cost.
        _markDispatchedSql = _options.DeleteOnDispatch
            ? $@"
            DELETE FROM {fullTableName} m
            USING unnest(@Ids, @CreatedAts) AS t(id, created_at)
            WHERE m.id = t.id AND m.created_at = t.created_at AND m.owner_id = @OwnerId;"
            : $@"
            UPDATE {fullTableName} m
            SET    state = 2, processed_at = NOW(), updated_at = NOW(), owner_id = NULL
            FROM   unnest(@Ids, @CreatedAts) AS t(id, created_at)
            WHERE  m.id = t.id AND m.created_at = t.created_at AND m.owner_id = @OwnerId;";

        _markFailedSql = $@"
            UPDATE {fullTableName} m
            SET    state = @State,
                   updated_at = NOW(),
                   deliver_at = CASE WHEN @State = 3 THEN NOW() + LEAST((2^LEAST(retry_count, 11)::bigint * 10), @MaxBackoffSeconds::bigint) * INTERVAL '1 second' ELSE deliver_at END,
                   error  = @Error,
                   retry_count = retry_count + 1,
                   owner_id = NULL
            FROM   unnest(@Ids, @CreatedAts) AS t(id, created_at)
            WHERE  m.id = t.id AND m.created_at = t.created_at AND m.state = 1 AND m.owner_id = @OwnerId;";

        // ISSUE-SQL3 FIX: LIMIT is now a parameter (@ReclaimLimit) instead of a hardcoded 1000.
        // Configurable via OutboxRuntimeOptions.ReclaimBatchLimit (default 1000).
        // In high-load environments with cascading crash scenarios, raise this to 5000+
        // to drain state=1 backlogs faster on restart.
        _reclaimSql = $@"
            WITH stale AS (
                SELECT id, created_at
                FROM   {fullTableName}
                WHERE  state = 1
                  AND  updated_at < NOW() - make_interval(secs => @StaleSeconds)
                  AND  created_at >= NOW() - make_interval(days => @MaxAgeDays)
                LIMIT  @ReclaimLimit
                FOR    UPDATE SKIP LOCKED
            ),
            reclaimed AS (
                UPDATE {fullTableName} m
                SET    state = 0, updated_at = NOW(), owner_id = NULL
                FROM   stale
                WHERE  m.id = stale.id AND m.created_at = stale.created_at
                RETURNING m.id
            )
            SELECT COUNT(*) FROM reclaimed;";

        _pendingCountSql = $@"
            -- Note: pg_stat_user_tables.n_live_tup estimates ALL rows in the table.
            -- Since this outbox uses Delete-on-Dispatch, the table only contains pending, 
            -- in-flight, failed, and dead-letter messages. Thus, n_live_tup is a reasonable 
            -- O(1) upper-bound estimate for pending count when the table is large.
            WITH estimate AS (
                SELECT COALESCE(SUM(n_live_tup), 0) AS cnt
                FROM pg_stat_user_tables
                WHERE schemaname = @Schema
                  AND (relname = @Table OR (relname LIKE @TablePrefix AND relname NOT LIKE '%_dead_letters%'))
            )
            SELECT CASE
                WHEN (SELECT cnt FROM estimate) >= @Threshold THEN (SELECT cnt FROM estimate)
                ELSE (SELECT COUNT(*) FROM {fullTableName} WHERE state IN (0, 3))
            END;";

        _insertBulkSql = $"COPY {fullTableName} (id, type, payload, correlation_id, causation_id, headers_json, state, created_at, updated_at, deliver_at, retry_count) FROM STDIN (FORMAT BINARY)";

        _purgeDispatchedSql = $@"
            WITH to_purge AS (
                SELECT id, created_at
                FROM   {fullTableName}
                WHERE  state = 2
                  AND  (processed_at < @Cutoff OR (processed_at IS NULL AND updated_at < @Cutoff))
                ORDER  BY created_at ASC
                LIMIT  @BatchSize
            )
            DELETE FROM {fullTableName} m
            USING to_purge
            WHERE m.id = to_purge.id AND m.created_at = to_purge.created_at;";
    }

    /// <inheritdoc/>

    public async ValueTask InsertAsync(
        OutboxMessage record,
        IOutboxTransactionContext transaction,
        CancellationToken cancellationToken = default)
    {

        var conn = transaction.Connection as NpgsqlConnection
            ?? throw new InvalidOperationException("Transaction must be associated with an NpgsqlConnection.");

        var npgsqlTx = transaction.Transaction as NpgsqlTransaction;
        await using var cmd = new NpgsqlCommand(_insertSql, conn, npgsqlTx);


        var payloadArray = record.Payload.ToByteArray();
        var headersArray = record.Headers.ToByteArray();

        cmd.Parameters.Add(new NpgsqlParameter("Id", NpgsqlTypes.NpgsqlDbType.Uuid) { Value = record.Id });
        cmd.Parameters.Add(new NpgsqlParameter("MessageType", NpgsqlTypes.NpgsqlDbType.Varchar) { Value = record.MessageType });

        // Pass payload as raw bytes mapped to jsonb
        cmd.Parameters.Add(new NpgsqlParameter("Payload", NpgsqlTypes.NpgsqlDbType.Jsonb) { Value = payloadArray });


        cmd.Parameters.Add(new NpgsqlParameter("CorrelationId", NpgsqlTypes.NpgsqlDbType.Varchar) { Value = record.CorrelationId ?? (object)DBNull.Value });

        cmd.Parameters.Add(new NpgsqlParameter("CausationId", NpgsqlTypes.NpgsqlDbType.Varchar) { Value = record.CausationId ?? (object)DBNull.Value });
        cmd.Parameters.Add(new NpgsqlParameter("HeadersJson", NpgsqlTypes.NpgsqlDbType.Jsonb) { Value = headersArray });
        cmd.Parameters.Add(new NpgsqlParameter("CreatedAt", NpgsqlTypes.NpgsqlDbType.TimestampTz) { Value = record.CreatedAt });

        cmd.Parameters.Add(new NpgsqlParameter("DeliverAt", NpgsqlTypes.NpgsqlDbType.TimestampTz) { Value = record.DeliverAt.HasValue ? (object)record.DeliverAt.Value : DBNull.Value });

        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// <b>Transactional guarantee</b>: Uses a single <c>INSERT ... SELECT unnest()</c>
    /// statement executed within the caller's <paramref name="transaction"/>. Unlike BINARY COPY,
    /// this participates fully in the caller's database transaction Ã¢â‚¬â€ a rollback on the business
    /// operation will also roll back these inserts.
    /// </para>
    /// <para>
    /// For non-transactional bulk import (e.g., data migration or seeding), use
    /// <see cref="InsertBulkAsync"/> which uses COPY BINARY but explicitly documents
    /// that it does NOT participate in external transactions.
    /// </para>
    /// </remarks>

    public async ValueTask InsertBatchAsync(
        ReadOnlyMemory<OutboxMessage> records,
        IOutboxTransactionContext transaction,
        CancellationToken cancellationToken = default)
    {
        if (records.IsEmpty) return;


        var conn = transaction.Connection as NpgsqlConnection
            ?? throw new InvalidOperationException("Transaction must be associated with an NpgsqlConnection.");

        var npgsqlTx = transaction.Transaction as NpgsqlTransaction;
        await using var cmd = new NpgsqlCommand(_insertBatchSql, conn, npgsqlTx);

        var span = records.Span;
        int count = records.Length;

        // Pre-allocate arrays for UNNEST parameters Ã¢â‚¬â€ one allocation per batch, not per message.
        var ids = new Guid[count];
        var types = new string[count];
        var payloads = new byte[count][];
        var correlationIds = new string?[count];
        var causationIds = new string?[count];
        var headers = new byte[count][];
        var createdAts = new DateTimeOffset[count];
        var deliverAts = new DateTimeOffset?[count];


        for (int i = 0; i < count; i++)
        {
            var record = span[i];
            ids[i] = record.Id;
            types[i] = record.MessageType;
            payloads[i] = record.Payload.ToByteArray();
            correlationIds[i] = record.CorrelationId;
            causationIds[i] = record.CausationId;
            headers[i] = record.Headers.ToByteArray();
            createdAts[i] = record.CreatedAt;
            deliverAts[i] = record.DeliverAt;
        }

        // P2-E FIX: Assert all UNNEST arrays have equal length.
        // A mismatch produces a cryptic "unnest() requires arrays of the same length" from PostgreSQL.
        // This assertion surfaces the bug immediately in Debug builds with a clear diagnostic.
        // Stryker disable all 
        System.Diagnostics.Debug.Assert(
            ids.Length == types.Length &&
            ids.Length == payloads.Length &&
            ids.Length == correlationIds.Length &&
            ids.Length == causationIds.Length &&
            ids.Length == headers.Length &&
            ids.Length == createdAts.Length &&
            ids.Length == deliverAts.Length,
            $"InsertBatchAsync: all UNNEST parameter arrays must have equal length ({count}). " +
            "A length mismatch means a bug in the array-building loop above.");
        // Stryker restore all

        cmd.Parameters.Add(new NpgsqlParameter("Ids", NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Uuid) { Value = ids });
        cmd.Parameters.Add(new NpgsqlParameter("Types", NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Varchar) { Value = types });
        cmd.Parameters.Add(new NpgsqlParameter("Payloads", NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Jsonb) { Value = payloads });

        cmd.Parameters.Add(new NpgsqlParameter("CorrelationIds", NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Varchar) { Value = correlationIds });

        cmd.Parameters.Add(new NpgsqlParameter("CausationIds", NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Varchar) { Value = causationIds });
        cmd.Parameters.Add(new NpgsqlParameter("Headers", NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Jsonb) { Value = headers });
        cmd.Parameters.Add(new NpgsqlParameter("CreatedAts", NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.TimestampTz) { Value = createdAts });

        cmd.Parameters.Add(new NpgsqlParameter("DeliverAts", NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.TimestampTz) { Value = deliverAts });

        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Bulk-imports messages using PostgreSQL BINARY COPY for maximum throughput.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>WARNING Ã¢â‚¬â€ NOT TRANSACTIONAL</b>: BINARY COPY does not participate in external
    /// database transactions. Intended exclusively for non-transactional
    /// bulk imports such as data migration, seeding, or administrative imports where
    /// the caller explicitly accepts the absence of transactional rollback.
    /// </para>
    /// <para>
    /// For normal Outbox usage (inside a business transaction), use
    /// <see cref="InsertBatchAsync"/> which is fully transactional.
    /// </para>
    /// </remarks>
    public async Task InsertBulkAsync(
        IReadOnlyList<OutboxMessage> records,
        CancellationToken cancellationToken = default)
    {
        if (records.Count == 0) return;

        await using var conn = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        // P2-F FIX: This method opens its OWN fresh connection from _dataSource, which guarantees
        // there is no ambient transaction. BINARY COPY is not transactional â€” it cannot be rolled
        // back. Do NOT refactor this to reuse a caller-supplied connection, as that would silently
        // break the non-transactional guarantee. For transactional bulk import, use InsertBatchAsync.
        await using var writer = await conn.BeginBinaryImportAsync(
            _insertBulkSql,
            cancellationToken).ConfigureAwait(false);

        for (int i = 0; i < records.Count; i++)
        {
            var record = records[i];

            await writer.StartRowAsync(cancellationToken).ConfigureAwait(false);
            await writer.WriteAsync(record.Id, NpgsqlTypes.NpgsqlDbType.Uuid, cancellationToken).ConfigureAwait(false);
            await writer.WriteAsync(record.MessageType, NpgsqlTypes.NpgsqlDbType.Varchar, cancellationToken).ConfigureAwait(false);
            var payloadBytes = record.Payload.ToByteArray();
            await writer.WriteAsync(payloadBytes, NpgsqlTypes.NpgsqlDbType.Jsonb, cancellationToken).ConfigureAwait(false);

            if (record.CorrelationId != null)
                await writer.WriteAsync(record.CorrelationId, NpgsqlTypes.NpgsqlDbType.Varchar, cancellationToken).ConfigureAwait(false);
            else
                await writer.WriteNullAsync(cancellationToken).ConfigureAwait(false);

            if (record.CausationId != null)
                await writer.WriteAsync(record.CausationId, NpgsqlTypes.NpgsqlDbType.Varchar, cancellationToken).ConfigureAwait(false);
            else
                await writer.WriteNullAsync(cancellationToken).ConfigureAwait(false);

            var headersBytes = record.Headers.ToByteArray();

            await writer.WriteAsync(headersBytes, NpgsqlTypes.NpgsqlDbType.Jsonb, cancellationToken).ConfigureAwait(false);
            await writer.WriteAsync(0, NpgsqlTypes.NpgsqlDbType.Integer, cancellationToken).ConfigureAwait(false);
            await writer.WriteAsync(record.CreatedAt, NpgsqlTypes.NpgsqlDbType.TimestampTz, cancellationToken).ConfigureAwait(false);
            await writer.WriteAsync(record.CreatedAt, NpgsqlTypes.NpgsqlDbType.TimestampTz, cancellationToken).ConfigureAwait(false);

            if (record.DeliverAt.HasValue)
                await writer.WriteAsync(record.DeliverAt.Value, NpgsqlTypes.NpgsqlDbType.TimestampTz, cancellationToken).ConfigureAwait(false);
            else
                await writer.WriteNullAsync(cancellationToken).ConfigureAwait(false);

            await writer.WriteAsync(0, NpgsqlTypes.NpgsqlDbType.Integer, cancellationToken).ConfigureAwait(false);
        }

        await writer.CompleteAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async ValueTask<IReadOnlyList<OutboxMessage>> FetchPendingAsync(
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand(_fetchPendingSql, conn);
        cmd.Parameters.Add(new NpgsqlParameter("BatchSize", NpgsqlTypes.NpgsqlDbType.Integer) { Value = batchSize });
        cmd.Parameters.Add(new NpgsqlParameter("OwnerId", NpgsqlTypes.NpgsqlDbType.Uuid) { Value = _instanceId });
        // Partition pruning: restrict to the MaxAgeDays window.
        cmd.Parameters.Add(new NpgsqlParameter("MaxAgeDays", NpgsqlTypes.NpgsqlDbType.Integer) { Value = (int)_options.MaxMessageAge.TotalDays });

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var list = new List<OutboxMessage>(batchSize);

        int idOrd = reader.GetOrdinal("id");
        int typeOrd = reader.GetOrdinal("type");
        int payloadOrd = reader.GetOrdinal("payload");
        int correlationIdOrd = reader.GetOrdinal("correlation_id");
        int causationIdOrd = reader.GetOrdinal("causation_id");
        int headersJsonOrd = reader.GetOrdinal("headers_json");
        int createdAtOrd = reader.GetOrdinal("created_at");
        int processedAtOrd = reader.GetOrdinal("processed_at");
        int deliverAtOrd = reader.GetOrdinal("deliver_at");
        int stateOrd = reader.GetOrdinal("state");
        int errorOrd = reader.GetOrdinal("error");
        int retryCountOrd = reader.GetOrdinal("retry_count");

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var id = reader.GetGuid(idOrd);
            var messageType = reader.GetString(typeOrd);
            var payload = reader.IsDBNull(payloadOrd) ? "{}"u8.ToArray() : reader.GetFieldValue<byte[]>(payloadOrd);

            var correlationId = reader.IsDBNull(correlationIdOrd) ? null : reader.GetString(correlationIdOrd);

            var causationId = reader.IsDBNull(causationIdOrd) ? null : reader.GetString(causationIdOrd);
            var headersJson = reader.IsDBNull(headersJsonOrd) ? "{}"u8.ToArray() : reader.GetFieldValue<byte[]>(headersJsonOrd);
            var createdAt = reader.GetFieldValue<DateTimeOffset>(createdAtOrd);

            var processedAt = reader.IsDBNull(processedAtOrd) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(processedAtOrd);

            var deliverAt = reader.IsDBNull(deliverAtOrd) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(deliverAtOrd);
            var state = reader.GetInt32(stateOrd);
            // F-03 AUDIT FIX: Validate the state returned by FetchPendingAsync.
            //
            // Expected post-UPDATE states are only 0 (Pending) or 1 (InFlight):
            //   - 0 (Pending): can happen if the UPDATE races with another consumer's delete/reclaim.
            //   - 1 (InFlight): the normal post-UPDATE state.
            //
            // State 3 (Failed) and 4 (DeadLettered) should NEVER appear here because the CTE
            // UPDATE atomically transitions the row from state IN (0,3) to state=1. If state=3 or
            // state=4 is returned, it indicates a bug in a custom IOutboxRepository implementation
            // or an unexpected concurrent modification.
            //
            // The previous implementation accepted states {0,1,3,4} silently. This version:
            //   - Still accepts {0,1,3,4} to avoid breaking custom implementations
            //   - Emits a Debug.Assert in development builds to surface the bug immediately
            //   - Emits Trace.TraceWarning for production monitoring via TraceListener
            // Stryker disable all 
            if (!IsValidFetchedState(state))
            {
                // Emit a warning visible in both development (Debug.Assert) and production (TraceWarning).
                // Using Trace rather than ILogger to avoid a constructor breaking change.
                // In production, configure a System.Diagnostics.TraceListener to capture this.
                System.Diagnostics.Debug.Assert(
                    condition: false,
                    message: $"PostgreSqlOutboxRepository.FetchPendingAsync returned unexpected state={state} for message id={reader.GetGuid(idOrd)} ({reader.GetString(typeOrd)}). " +
                             "Expected state 0 (Pending) or 1 (InFlight) after the CTE UPDATE. " +
                             "This indicates a bug in a custom IOutboxRepository or unexpected concurrent modification.");
                System.Diagnostics.Trace.TraceWarning(
                    $"[EricksonLopez.Outbox] PostgreSqlOutboxRepository.FetchPendingAsync: unexpected state={state} for message id={reader.GetGuid(idOrd)} type={reader.GetString(typeOrd)}. Skipping row.");
                continue;
            }
            // Stryker restore all
            

            var error = reader.IsDBNull(errorOrd) ? null : reader.GetString(errorOrd);
            var retryCount = reader.GetInt32(retryCountOrd);

            list.Add(new OutboxMessage(
                Id: id,
                MessageType: messageType,
                Payload: payload,
                CorrelationId: correlationId,
                CausationId: causationId,
                Headers: headersJson,
                CreatedAt: createdAt,
                ProcessedAt: processedAt,
                DeliverAt: deliverAt,
                Status: (EricksonLopez.Outbox.OutboxMessageStatus)state,
                RetryCount: retryCount,
                Error: error
            ));
        }
        return list;
    }

    // F-03 AUDIT FIX: Renamed from IsValidState to IsValidFetchedState to more precisely
    // document that this validates states that are valid AFTER the FetchPendingAsync CTE UPDATE.
    // Expected: 0 (Pending) or 1 (InFlight) post-UPDATE. States 3 and 4 are defensively
    // accepted to handle edge cases with custom implementations but trigger warnings above.
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private static bool IsValidFetchedState(int state) => state is 0 or 1 or 3 or 4;

    /// <inheritdoc/>

    public async ValueTask MarkAsDispatchedAsync(
        IReadOnlyList<OutboxMessage> messages,
        CancellationToken cancellationToken = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand(_markDispatchedSql, conn);

        // Pre-size to avoid List<T> buffer resizing on full batches.
        int capacity = messages.Count;
        using var idList = new PooledList<Guid>(capacity);
        using var createdAtList = new PooledList<DateTimeOffset>(capacity);
        for (int i = 0; i < messages.Count; i++)
        {
            idList[i] = messages[i].Id;
            createdAtList[i] = messages[i].CreatedAt;
        }

        cmd.Parameters.Add(new NpgsqlParameter("Ids", NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Uuid) { Value = idList });
        cmd.Parameters.Add(new NpgsqlParameter("CreatedAts", NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.TimestampTz) { Value = createdAtList });
        cmd.Parameters.Add(new NpgsqlParameter("OwnerId", NpgsqlTypes.NpgsqlDbType.Uuid) { Value = _instanceId });

        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// Increments <c>retry_count</c> and applies an exponential backoff to <c>deliver_at</c>
    /// (capped at <see cref="OutboxRuntimeOptions.MaxBackoffSeconds"/>) when setting the
    /// Failed state. When <paramref name="isDeadLetter"/> is <see langword="true"/>, sets the state
    /// to the dead-letter value (4) and skips backoff scheduling.
    /// </para>
    /// </remarks>

    public async ValueTask MarkAsFailedAsync(
        IReadOnlyList<OutboxMessage> messages,
        string error,
        bool isDeadLetter = false,
        CancellationToken cancellationToken = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand(_markFailedSql, conn);

        // Pre-size to avoid List<T> buffer resizing on full batches.
        int capacity = messages.Count;
        using var idList = new PooledList<Guid>(capacity);
        using var createdAtList = new PooledList<DateTimeOffset>(capacity);
        for (int i = 0; i < messages.Count; i++)
        {
            idList[i] = messages[i].Id;
            createdAtList[i] = messages[i].CreatedAt;
        }

        cmd.Parameters.Add(new NpgsqlParameter("State", NpgsqlTypes.NpgsqlDbType.Integer) { Value = isDeadLetter ? 4 : 3 });
        cmd.Parameters.Add(new NpgsqlParameter("OwnerId", NpgsqlTypes.NpgsqlDbType.Uuid) { Value = _instanceId });
        cmd.Parameters.Add(new NpgsqlParameter("Error", NpgsqlTypes.NpgsqlDbType.Text) { Value = TruncateError(error) });
        cmd.Parameters.Add(new NpgsqlParameter("Ids", NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Uuid) { Value = idList });
        cmd.Parameters.Add(new NpgsqlParameter("CreatedAts", NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.TimestampTz) { Value = createdAtList });
        // P2-FIX: MaxBackoffSeconds is now configurable (default 3600s = 1 hour).
        cmd.Parameters.Add(new NpgsqlParameter("MaxBackoffSeconds", NpgsqlTypes.NpgsqlDbType.Integer) { Value = _options.MaxBackoffSeconds });

        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async ValueTask<int> ReclaimStaleMessagesAsync(
        TimeSpan staleTimeout,
        CancellationToken cancellationToken = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand(_reclaimSql, conn);
        cmd.Parameters.Add(new NpgsqlParameter("StaleSeconds", NpgsqlTypes.NpgsqlDbType.Integer) { Value = (int)staleTimeout.TotalSeconds });
        cmd.Parameters.Add(new NpgsqlParameter("MaxAgeDays", NpgsqlTypes.NpgsqlDbType.Integer) { Value = (int)_options.MaxMessageAge.TotalDays });
        // ISSUE-SQL3 FIX: Use configurable limit instead of the hardcoded 1000 in SQL.
        cmd.Parameters.Add(new NpgsqlParameter("ReclaimLimit", NpgsqlTypes.NpgsqlDbType.Integer) { Value = _options.ReclaimBatchLimit });

        var scalar = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return Convert.ToInt32(scalar, System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Dual-path implementation to avoid O(N) COUNT(*) on large tables.
    ///
    /// <para>
    /// For tables with an estimated row count >= LargeTableThreshold rows,
    /// returns the catalog estimate from <c>pg_stat_user_tables</c>.
    /// </para>
    ///
    /// <para>
    /// P2-FIX: For partitioned tables, the query aggregates <c>n_live_tup</c> from all child
    /// partitions (matching <c>LIKE 'messages%'</c>) instead of only the parent table.
    /// In PostgreSQL, the parent partitioned table always has <c>n_live_tup=0</c> â€” actual
    /// row counts live in the child partitions. Without this fix, the estimate was always
    /// 0 for partitioned deployments, causing the query to always take the slow exact-count path.
    /// </para>
    ///
    /// <para>
    /// <b>AUDIT-FIX: Upper-bound note</b>: When the catalog estimate path is taken (table &gt;=
    /// <c>LargeTableThreshold</c> rows), the returned value is an <b>upper bound</b>: it counts
    /// ALL live rows, including <c>state=1</c> (InFlight) and <c>state=4</c> (DeadLettered)
    /// â€” not just <c>state=0</c> (Pending) and <c>state=3</c> (Failed).
    /// With <c>DeleteOnDispatch=true</c> the over-estimate is typically small. However, in
    /// deployments with large in-flight backlogs, the metric may appear higher than actual
    /// pending count, potentially triggering false <c>Degraded</c> health check alerts.
    /// Configure health check thresholds with an appropriate margin.
    /// </para>
    /// </remarks>
    public async ValueTask<long> GetPendingCountAsync(CancellationToken cancellationToken = default)

    {
        await using var conn = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand(_pendingCountSql, conn);
        cmd.Parameters.Add(new NpgsqlParameter("Threshold", NpgsqlTypes.NpgsqlDbType.Bigint) { Value = (long)_options.LargeTableThreshold });
        cmd.Parameters.Add(new NpgsqlParameter("Schema", NpgsqlTypes.NpgsqlDbType.Text) { Value = _options.SchemaName });
        cmd.Parameters.Add(new NpgsqlParameter("Table", NpgsqlTypes.NpgsqlDbType.Text) { Value = _options.TableName });
        // Stryker disable once all 
        cmd.Parameters.Add(new NpgsqlParameter("TablePrefix", NpgsqlTypes.NpgsqlDbType.Text) { Value = _options.TableName + "_%" });

        var result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        if (result is null or DBNull) return 0;

        var count = Convert.ToInt64(result, System.Globalization.CultureInfo.InvariantCulture);
        return count;
    }
    /// <inheritdoc/>
    /// <remarks>
    /// PostgreSQL implementation: single-row SELECT by id. Because the table is partitioned
    /// by RANGE on <c>created_at</c>, a lookup by id alone requires scanning all partition
    /// children. For production use with very large tables, consider passing <c>created_at</c>
    /// as a hint to enable partition pruning via an overload.
    /// </remarks>
    public async ValueTask<OutboxMessage?> GetMessageAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var fullTableName = $"\"{_options.SchemaName}\".\"{_options.TableName}\"";

        var sql = $"""
            SELECT id, type, payload, correlation_id, causation_id, headers_json,
                   created_at, processed_at, deliver_at, state, error, retry_count
            FROM {fullTableName}
            WHERE id = @Id
            LIMIT 1;
            """;

        await using var conn = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.Add(new NpgsqlParameter("Id", NpgsqlTypes.NpgsqlDbType.Uuid) { Value = id });

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            return null;

        var idOrd = reader.GetOrdinal("id");
        var typeOrd = reader.GetOrdinal("type");
        var payloadOrd = reader.GetOrdinal("payload");
        var correlationIdOrd = reader.GetOrdinal("correlation_id");
        var causationIdOrd = reader.GetOrdinal("causation_id");
        var headersJsonOrd = reader.GetOrdinal("headers_json");
        var createdAtOrd = reader.GetOrdinal("created_at");
        var processedAtOrd = reader.GetOrdinal("processed_at");
        var deliverAtOrd = reader.GetOrdinal("deliver_at");
        var stateOrd = reader.GetOrdinal("state");
        var errorOrd = reader.GetOrdinal("error");
        var retryCountOrd = reader.GetOrdinal("retry_count");

        return new OutboxMessage(
            Id: reader.GetGuid(idOrd),
            MessageType: reader.GetString(typeOrd),
            Payload: reader.IsDBNull(payloadOrd) ? System.Text.Encoding.UTF8.GetBytes("{}") : reader.GetFieldValue<byte[]>(payloadOrd),
            CorrelationId: reader.IsDBNull(correlationIdOrd) ? null : reader.GetString(correlationIdOrd),
            CausationId: reader.IsDBNull(causationIdOrd) ? null : reader.GetString(causationIdOrd),
            Headers: reader.IsDBNull(headersJsonOrd) ? System.Text.Encoding.UTF8.GetBytes("{}") : reader.GetFieldValue<byte[]>(headersJsonOrd),
            CreatedAt: reader.GetFieldValue<DateTimeOffset>(createdAtOrd),
            ProcessedAt: reader.IsDBNull(processedAtOrd) ? null : reader.GetFieldValue<DateTimeOffset>(processedAtOrd),
            DeliverAt: reader.IsDBNull(deliverAtOrd) ? null : reader.GetFieldValue<DateTimeOffset>(deliverAtOrd),
            Status: (OutboxMessageStatus)reader.GetInt32(stateOrd),
            RetryCount: reader.GetInt32(retryCountOrd),
            Error: reader.IsDBNull(errorOrd) ? null : reader.GetString(errorOrd)
        );
    }

    /// <inheritdoc/>
    /// <remarks>
    /// S-01 AUDIT FIX: Partition-pruning implementation.
    /// When <paramref name="createdAtHint"/> is provided, adds AND created_at = @CreatedAt
    /// to the WHERE clause. For RANGE-partitioned tables (partitioned by created_at),
    /// this allows PostgreSQL's query planner to prune all partitions except the single target
    /// partition, turning an O(N-partitions) sequential scan into an O(1) index seek.
    /// When createdAtHint is null, falls back to the full-table scan path.
    /// </remarks>
    public async ValueTask<OutboxMessage?> GetMessageAsync(
        Guid id,
        DateTimeOffset? createdAtHint,
        CancellationToken cancellationToken = default)
    {
        if (createdAtHint is null)
            return await GetMessageAsync(id, cancellationToken).ConfigureAwait(false);

        var fullTableName = $"\"{_options.SchemaName}\".\"{_options.TableName}\"";
        var sql = $"SELECT id, type, payload, correlation_id, causation_id, headers_json, created_at, processed_at, deliver_at, state, error, retry_count FROM {fullTableName} WHERE id = @Id AND created_at = @CreatedAt LIMIT 1;";

        await using var conn = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.Add(new NpgsqlParameter("Id", NpgsqlTypes.NpgsqlDbType.Uuid) { Value = id });
        cmd.Parameters.Add(new NpgsqlParameter("CreatedAt", NpgsqlTypes.NpgsqlDbType.TimestampTz) { Value = createdAtHint.Value });

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            return null;

        return new OutboxMessage(
            Id: reader.GetGuid(reader.GetOrdinal("id")),
            MessageType: reader.GetString(reader.GetOrdinal("type")),
            Payload: reader.IsDBNull(reader.GetOrdinal("payload")) ? System.Text.Encoding.UTF8.GetBytes("{}") : reader.GetFieldValue<byte[]>(reader.GetOrdinal("payload")),
            CorrelationId: reader.IsDBNull(reader.GetOrdinal("correlation_id")) ? null : reader.GetString(reader.GetOrdinal("correlation_id")),
            CausationId: reader.IsDBNull(reader.GetOrdinal("causation_id")) ? null : reader.GetString(reader.GetOrdinal("causation_id")),
            Headers: reader.IsDBNull(reader.GetOrdinal("headers_json")) ? System.Text.Encoding.UTF8.GetBytes("{}") : reader.GetFieldValue<byte[]>(reader.GetOrdinal("headers_json")),
            CreatedAt: reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("created_at")),
            ProcessedAt: reader.IsDBNull(reader.GetOrdinal("processed_at")) ? null : reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("processed_at")),
            DeliverAt: reader.IsDBNull(reader.GetOrdinal("deliver_at")) ? null : reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("deliver_at")),
            Status: (OutboxMessageStatus)reader.GetInt32(reader.GetOrdinal("state")),
            RetryCount: reader.GetInt32(reader.GetOrdinal("retry_count")),
            Error: reader.IsDBNull(reader.GetOrdinal("error")) ? null : reader.GetString(reader.GetOrdinal("error"))
        );
    }

    /// <inheritdoc/>
    public async ValueTask<int> PurgeDispatchedMessagesAsync(
        DateTimeOffset cutoff,
        int batchSize = 1000,
        CancellationToken cancellationToken = default)
    {
        if (batchSize <= 0) return 0;

        await using var conn = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand(_purgeDispatchedSql, conn);
        cmd.Parameters.Add(new NpgsqlParameter("Cutoff", NpgsqlTypes.NpgsqlDbType.TimestampTz) { Value = cutoff });
        cmd.Parameters.Add(new NpgsqlParameter("BatchSize", NpgsqlTypes.NpgsqlDbType.Integer) { Value = batchSize });

        return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Truncates an error string to fit the 4000-character database column limit,
    /// preserving the beginning and end of the message for maximum diagnostic value.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The first 3500 characters typically contain the exception type and primary message.
    /// The last 449 characters typically contain the innermost stack frames.
    /// A truncation marker is inserted at the cut point so operators know the message was truncated.
    /// </para>
    /// </remarks>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private static string? TruncateError(string? error)
    {
        if (error is null || error.Length <= 4000) return error;
        const string marker = " ... [TRUNCATED] ... ";
        // Preserve first 3500 chars + marker (21 chars) + last 449 chars = 3970 chars total (< 4000)
        return string.Concat(error.AsSpan(0, 3530), marker, error.AsSpan(error.Length - 449));
    }

    [System.Text.RegularExpressions.GeneratedRegex("^[a-zA-Z0-9_]+$", System.Text.RegularExpressions.RegexOptions.None, 1000)]
    private static partial System.Text.RegularExpressions.Regex SchemaNameRegex();


    internal sealed class PooledList<T> : IList<T>, IDisposable
    {
        private T[]? _array;
        private readonly int _count;

        public PooledList(int count)
        {
            _array = System.Buffers.ArrayPool<T>.Shared.Rent(count);
            _count = count;
        }

        public T this[int index]
        {
            get
            {
                if ((uint)index >= (uint)_count) throw new ArgumentOutOfRangeException(nameof(index));
                return _array![index];
            }
            set
            {
                if ((uint)index >= (uint)_count) throw new ArgumentOutOfRangeException(nameof(index));
                _array![index] = value;
            }
        }

        public int Count => _count;
        public bool IsReadOnly => false;
        internal bool IsDisposed => _array == null;

        public void Add(T item) => throw new NotSupportedException();
        public void Clear() => throw new NotSupportedException();
        public bool Contains(T item) => throw new NotSupportedException();
        public void CopyTo(T[] array, int arrayIndex) => Array.Copy(_array!, 0, array, arrayIndex, _count);
        public IEnumerator<T> GetEnumerator()
        {
            for (int i = 0; i < _count; i++) yield return _array![i];
        }
        public int IndexOf(T item) => throw new NotSupportedException();
        public void Insert(int index, T item) => throw new NotSupportedException();
        public bool Remove(T item) => throw new NotSupportedException();
        public void RemoveAt(int index) => throw new NotSupportedException();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

        public void Dispose()
        {
            if (_array != null)
            {
                // P2-5 AUDIT FIX: Clear array before returning to pool for consistency
                // with the project's convention (see OutboxMessageBuilder). Prevents
                // stale data from leaking across unrelated pool consumers.
                // Stryker disable once all 
                System.Buffers.ArrayPool<T>.Shared.Return(_array, clearArray: true);
                _array = null;
            }
        }
    }
}






