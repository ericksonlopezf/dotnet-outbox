// Copyright © Erickson Lopez. MIT License.
using System;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using EricksonLopez.Outbox;
using Microsoft.Data.SqlClient;

namespace EricksonLopez.Outbox.Storage.SqlServer.Tests;

public static class SqlServerTestDatabase
{
    public const string Schema = "outbox";
    public const string DefaultOutboxTable = "outbox_messages";

    public static async Task EnsureSchemaAsync(string connectionString, string tableName = "outbox_messages")
    {
        using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        var schemaSql = $@"
            IF SCHEMA_ID('outbox') IS NULL
            BEGIN
                EXEC('CREATE SCHEMA [outbox]');
            END

            IF TYPE_ID('outbox.MessageKeysType') IS NULL
            BEGIN
                CREATE TYPE outbox.MessageKeysType AS TABLE (
                    [Id] UNIQUEIDENTIFIER NOT NULL,
                    [CreatedAt] DATETIMEOFFSET NOT NULL,
                    PRIMARY KEY ([Id], [CreatedAt])
                );
            END

            IF OBJECT_ID('outbox.{tableName}', 'U') IS NULL
            BEGIN
                CREATE TABLE [outbox].[{tableName}] (
                    id UNIQUEIDENTIFIER PRIMARY KEY,
                    type NVARCHAR(255) NOT NULL,
                    payload VARBINARY(MAX),
                    correlation_id NVARCHAR(255),
                    causation_id NVARCHAR(255),
                    headers_json VARBINARY(MAX),
                    created_at DATETIMEOFFSET NOT NULL,
                    updated_at DATETIMEOFFSET NOT NULL,
                    processed_at DATETIMEOFFSET,
                    deliver_at DATETIMEOFFSET,
                    state INT NOT NULL,
                    retry_count INT NOT NULL DEFAULT 0,
                    owner_id UNIQUEIDENTIFIER,
                    error NVARCHAR(MAX)
                );
            END
            ELSE
            BEGIN
                TRUNCATE TABLE [outbox].[{tableName}];
            END

            IF OBJECT_ID('outbox.messages_dead_letters', 'U') IS NULL
            BEGIN
                CREATE TABLE [outbox].[messages_dead_letters] (
                    id UNIQUEIDENTIFIER PRIMARY KEY,
                    original_message_id UNIQUEIDENTIFIER NOT NULL,
                    type NVARCHAR(255) NOT NULL,
                    payload NVARCHAR(MAX),
                    correlation_id NVARCHAR(255),
                    causation_id NVARCHAR(255),
                    headers_json NVARCHAR(MAX),
                    created_at DATETIMEOFFSET NOT NULL,
                    dead_lettered_at DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
                    retry_count INT NOT NULL,
                    reason NVARCHAR(MAX),
                    last_error NVARCHAR(MAX)
                );
            END
            ELSE
            BEGIN
                TRUNCATE TABLE [outbox].[messages_dead_letters];
            END

            IF OBJECT_ID('dbo.messages_dead_letters', 'U') IS NULL
            BEGIN
                CREATE TABLE [dbo].[messages_dead_letters] (
                    id UNIQUEIDENTIFIER PRIMARY KEY,
                    original_message_id UNIQUEIDENTIFIER NOT NULL,
                    type NVARCHAR(255) NOT NULL,
                    payload NVARCHAR(MAX),
                    correlation_id NVARCHAR(255),
                    causation_id NVARCHAR(255),
                    headers_json NVARCHAR(MAX),
                    created_at DATETIMEOFFSET NOT NULL,
                    dead_lettered_at DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
                    retry_count INT NOT NULL,
                    reason NVARCHAR(MAX),
                    last_error NVARCHAR(MAX)
                );
            END
            ELSE
            BEGIN
                TRUNCATE TABLE [dbo].[messages_dead_letters];
            END

            IF OBJECT_ID('outbox.messages_idempotency', 'U') IS NULL
            BEGIN
                CREATE TABLE [outbox].[messages_idempotency] (
                    message_id NVARCHAR(255) NOT NULL,
                    consumer_id NVARCHAR(255) NOT NULL,
                    processed_at DATETIMEOFFSET NOT NULL,
                    PRIMARY KEY (message_id, consumer_id)
                );
            END
            ELSE
            BEGIN
                TRUNCATE TABLE [outbox].[messages_idempotency];
            END

            IF OBJECT_ID('dbo.messages_idempotency', 'U') IS NULL
            BEGIN
                CREATE TABLE [dbo].[messages_idempotency] (
                    message_id NVARCHAR(255) NOT NULL,
                    consumer_id NVARCHAR(255) NOT NULL,
                    processed_at DATETIMEOFFSET NOT NULL,
                    PRIMARY KEY (message_id, consumer_id)
                );
            END
            ELSE
            BEGIN
                TRUNCATE TABLE [dbo].[messages_idempotency];
            END
        ";

        await connection.ExecuteAsync(schemaSql);
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



