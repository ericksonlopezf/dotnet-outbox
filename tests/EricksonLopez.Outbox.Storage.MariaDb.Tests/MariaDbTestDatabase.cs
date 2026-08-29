// Copyright © Erickson Lopez. MIT License.
using System;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using EricksonLopez.Outbox;
using MySqlConnector;
using Testcontainers.MariaDb;

namespace EricksonLopez.Outbox.Storage.MariaDb.Tests;

public static class MariaDbTestDatabase
{
    public static async Task EnsureSchemaAsync(string connectionString, string tableName = "outbox_messages")
    {
        using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync();

        var schema = $@"
            CREATE TABLE IF NOT EXISTS {tableName} (
                id VARCHAR(36) PRIMARY KEY,
                type VARCHAR(255) NOT NULL,
                payload LONGBLOB,
                correlation_id VARCHAR(255),
                causation_id VARCHAR(255),
                headers_json LONGBLOB,
                created_at DATETIME(6) NOT NULL,
                updated_at DATETIME(6) NOT NULL,
                processed_at DATETIME(6),
                deliver_at DATETIME(6),
                state INT NOT NULL,
                retry_count INT NOT NULL DEFAULT 0,
                owner_id VARCHAR(255),
                error LONGTEXT
            );

            CREATE TABLE IF NOT EXISTS {tableName}_dead_letters (
                id VARCHAR(36) PRIMARY KEY,
                original_message_id VARCHAR(36) NOT NULL,
                type VARCHAR(255) NOT NULL,
                payload LONGBLOB,
                correlation_id VARCHAR(255),
                causation_id VARCHAR(255),
                headers_json LONGBLOB,
                created_at DATETIME(6) NOT NULL,
                dead_lettered_at DATETIME(6) NOT NULL,
                retry_count INT NOT NULL DEFAULT 0,
                reason LONGTEXT,
                last_error LONGTEXT
            );

            CREATE TABLE IF NOT EXISTS {tableName}_idempotency (
                message_id VARCHAR(36) NOT NULL,
                consumer_id VARCHAR(255) NOT NULL,
                processed_at DATETIME(6) NOT NULL,
                PRIMARY KEY (message_id, consumer_id)
            );

            CREATE DATABASE IF NOT EXISTS custom_schema;

            CREATE TABLE IF NOT EXISTS `custom_schema`.`{tableName}_dead_letters` (
                id VARCHAR(36) PRIMARY KEY,
                original_message_id VARCHAR(36) NOT NULL,
                type VARCHAR(255) NOT NULL,
                payload LONGBLOB,
                correlation_id VARCHAR(255),
                causation_id VARCHAR(255),
                headers_json LONGBLOB,
                created_at DATETIME(6) NOT NULL,
                dead_lettered_at DATETIME(6) NOT NULL,
                retry_count INT NOT NULL DEFAULT 0,
                reason LONGTEXT,
                last_error LONGTEXT
            );

            CREATE TABLE IF NOT EXISTS `custom_schema`.`{tableName}_idempotency` (
                message_id VARCHAR(36) NOT NULL,
                consumer_id VARCHAR(255) NOT NULL,
                processed_at DATETIME(6) NOT NULL,
                PRIMARY KEY (message_id, consumer_id)
            );

            DELETE FROM {tableName};
            DELETE FROM {tableName}_dead_letters;
            DELETE FROM {tableName}_idempotency;
            DELETE FROM `custom_schema`.`{tableName}_dead_letters`;
            DELETE FROM `custom_schema`.`{tableName}_idempotency`;
        ";

        await connection.ExecuteAsync(schema);
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
