// Copyright © Erickson Lopez. MIT License.
using System;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using EricksonLopez.Outbox;
using Npgsql;

namespace EricksonLopez.Outbox.Storage.PostgreSql.Tests;

public static class PostgreSqlTestDatabase
{
    public const string Schema = "outbox";
    public const string MessagesTable = "messages";
    public const string DeadLettersTable = "messages_dead_letters";
    public const string IdempotencyTable = "messages_idempotency";

    public static async Task EnsureSchemaAsync(NpgsqlDataSource dataSource)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        
        const string sql = @"
            CREATE SCHEMA IF NOT EXISTS outbox;

            CREATE TABLE IF NOT EXISTS outbox.messages (
                id UUID,
                type VARCHAR(255) NOT NULL,
                payload JSONB,
                correlation_id VARCHAR(255),
                causation_id VARCHAR(255),
                headers_json JSONB,
                created_at TIMESTAMPTZ NOT NULL,
                updated_at TIMESTAMPTZ NOT NULL,
                processed_at TIMESTAMPTZ,
                deliver_at TIMESTAMPTZ,
                state INT NOT NULL,
                retry_count INT NOT NULL DEFAULT 0,
                owner_id UUID,
                error TEXT,
                PRIMARY KEY (id, created_at)
            );

            CREATE TABLE IF NOT EXISTS outbox.messages_dead_letters (
                id UUID PRIMARY KEY,
                original_message_id UUID NOT NULL,
                type VARCHAR(255) NOT NULL,
                payload JSONB,
                correlation_id VARCHAR(255),
                causation_id VARCHAR(255),
                headers_json JSONB,
                created_at TIMESTAMPTZ NOT NULL,
                dead_lettered_at TIMESTAMPTZ NOT NULL,
                retry_count INT NOT NULL,
                error_reason TEXT NOT NULL,
                last_error TEXT
            );

            CREATE TABLE IF NOT EXISTS outbox.messages_idempotency (
                message_id VARCHAR(255) NOT NULL,
                consumer_id VARCHAR(255) NOT NULL,
                processed_at TIMESTAMPTZ NOT NULL,
                PRIMARY KEY (message_id, consumer_id)
            );

            TRUNCATE TABLE outbox.messages, outbox.messages_dead_letters, outbox.messages_idempotency;
        ";

        await connection.ExecuteAsync(sql);
    }

    public static OutboxMessage CreateMessage(
        Guid? id = null,
        string type = "order.created",
        byte[]? payload = null,
        OutboxMessageStatus state = OutboxMessageStatus.Pending,
        DateTimeOffset? createdAt = null,
        DateTimeOffset? deliverAt = null,
        int retryCount = 0,
        string? correlationId = null,
        string? causationId = null)
    {
        var now = createdAt ?? DateTimeOffset.UtcNow;
        return new OutboxMessage(
            id ?? Guid.NewGuid(),
            type,
            payload ?? System.Text.Encoding.UTF8.GetBytes("{}"),
            correlationId,
            causationId,
            ReadOnlyMemory<byte>.Empty,
            now,
            null,
            deliverAt,
            state,
            retryCount,
            null);
    }
}



