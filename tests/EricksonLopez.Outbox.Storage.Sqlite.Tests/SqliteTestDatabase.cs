// Copyright © Erickson Lopez. MIT License.
using System;
using System.Data;
using Dapper;
using EricksonLopez.Outbox;

namespace EricksonLopez.Outbox.Storage.Sqlite.Tests;

public static class SqliteTestDatabase
{
    public static void EnsureSchema(IDbConnection connection)
    {
        const string schema = @"
            CREATE TABLE IF NOT EXISTS messages (
                id TEXT PRIMARY KEY,
                type TEXT NOT NULL,
                payload BLOB,
                correlation_id TEXT,
                causation_id TEXT,
                headers_json BLOB,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL,
                processed_at TEXT,
                deliver_at TEXT,
                state INTEGER NOT NULL,
                retry_count INTEGER NOT NULL DEFAULT 0,
                owner_id TEXT,
                error TEXT
            );

            CREATE TABLE IF NOT EXISTS messages_dead_letters (
                id TEXT PRIMARY KEY,
                original_message_id TEXT NOT NULL,
                type TEXT NOT NULL,
                payload BLOB,
                correlation_id TEXT,
                causation_id TEXT,
                headers_json BLOB,
                created_at TEXT NOT NULL,
                dead_lettered_at TEXT NOT NULL,
                retry_count INTEGER NOT NULL DEFAULT 0,
                reason TEXT,
                last_error TEXT
            );

            CREATE TABLE IF NOT EXISTS messages_idempotency (
                message_id TEXT NOT NULL,
                consumer_id TEXT NOT NULL,
                processed_at TEXT NOT NULL,
                PRIMARY KEY (message_id, consumer_id)
            );

            DELETE FROM messages;
            DELETE FROM messages_dead_letters;
            DELETE FROM messages_idempotency;
        ";

        connection.Execute(schema);
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
