// Copyright © Erickson Lopez. MIT License.
using System;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using EricksonLopez.Outbox;
using Oracle.ManagedDataAccess.Client;

namespace EricksonLopez.Outbox.Storage.Oracle.Tests;

public static class OracleTestDatabase
{
    public static async Task EnsureSchemaAsync(string connectionString, string tableName = "messages")
    {
        using var connection = new OracleConnection(connectionString);
        await connection.OpenAsync();

        var schema = $@"
            BEGIN
                EXECUTE IMMEDIATE 'CREATE TABLE ""{tableName}"" (
                    id RAW(16) PRIMARY KEY,
                    type VARCHAR2(255) NOT NULL,
                    payload BLOB,
                    correlation_id VARCHAR2(255),
                    causation_id VARCHAR2(255),
                    headers_json BLOB,
                    created_at TIMESTAMP(6) WITH TIME ZONE NOT NULL,
                    updated_at TIMESTAMP(6) WITH TIME ZONE NOT NULL,
                    processed_at TIMESTAMP(6) WITH TIME ZONE,
                    deliver_at TIMESTAMP(6) WITH TIME ZONE,
                    state NUMBER(10) NOT NULL,
                    retry_count NUMBER(10) DEFAULT 0 NOT NULL,
                    owner_id RAW(16),
                    error CLOB
                )';
            EXCEPTION
                WHEN OTHERS THEN
                    IF SQLCODE != -955 THEN
                        RAISE;
                    END IF;
            END;";

        var deadLettersSchema = @"
            BEGIN
                EXECUTE IMMEDIATE 'CREATE TABLE ""MESSAGES_DEAD_LETTERS"" (
                    id VARCHAR2(32) PRIMARY KEY,
                    original_message_id VARCHAR2(32) NOT NULL,
                    type VARCHAR2(255) NOT NULL,
                    payload CLOB,
                    correlation_id VARCHAR2(255),
                    causation_id VARCHAR2(255),
                    headers_json CLOB,
                    created_at TIMESTAMP(6) WITH TIME ZONE NOT NULL,
                    dead_lettered_at TIMESTAMP(6) WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP NOT NULL,
                    retry_count NUMBER(10) DEFAULT 0 NOT NULL,
                    reason VARCHAR2(2000),
                    last_error CLOB
                )';
            EXCEPTION
                WHEN OTHERS THEN
                    IF SQLCODE != -955 THEN
                        RAISE;
                    END IF;
            END;";

        var idempotencySchema = @"
            BEGIN
                EXECUTE IMMEDIATE 'CREATE TABLE ""MESSAGES_IDEMPOTENCY"" (
                    message_id VARCHAR2(255) NOT NULL,
                    consumer_id VARCHAR2(255) NOT NULL,
                    processed_at TIMESTAMP(6) WITH TIME ZONE NOT NULL,
                    PRIMARY KEY (message_id, consumer_id)
                )';
            EXCEPTION
                WHEN OTHERS THEN
                    IF SQLCODE != -955 THEN
                        RAISE;
                    END IF;
            END;";

        await connection.ExecuteAsync(schema);
        await connection.ExecuteAsync(deadLettersSchema);
        await connection.ExecuteAsync(idempotencySchema);

        await connection.ExecuteAsync($"TRUNCATE TABLE \"{tableName}\"");
        await connection.ExecuteAsync("TRUNCATE TABLE \"MESSAGES_DEAD_LETTERS\"");
        await connection.ExecuteAsync("TRUNCATE TABLE \"MESSAGES_IDEMPOTENCY\"");
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



