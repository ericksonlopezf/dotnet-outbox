-- =============================================================================
-- 01_Init_Outbox.sql
-- EricksonLopez.Outbox — SQL Server Initial Schema
-- Requires: SQL Server 2019+ (for JSON support and SKIP LOCKED equivalent)
-- =============================================================================

-- Create the schema if it doesn't exist
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'outbox')
BEGIN
    EXEC('CREATE SCHEMA [outbox]')
END
GO

-- =============================================================================
-- 1. Outbox Messages table
-- Column notes:
--   type          → message type alias (e.g. "order.created.v1"), NOT CLR type name
--   payload       → NVARCHAR(MAX) storing JSON (SQL Server 2022+ supports JSON type)
--   state         → 0=Pending, 1=InFlight, 2=Dispatched, 3=Failed, 4=DeadLettered
--   deliver_at    → NULL = immediate delivery; non-NULL = scheduled delivery
--   retry_count   → incremented on each dispatch failure
--   error         → last known error message, nullable
-- =============================================================================
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE object_id = OBJECT_ID('[outbox].[messages]'))
BEGIN
    CREATE TABLE [outbox].[messages] (
        [id]             UNIQUEIDENTIFIER NOT NULL,
        [type]           NVARCHAR(255)    NOT NULL,
        [payload]        NVARCHAR(MAX)    NOT NULL,   -- JSON payload
        [correlation_id] NVARCHAR(255)    NULL,
        [causation_id]   NVARCHAR(255)    NULL,
        [headers_json]   NVARCHAR(MAX)    NOT NULL DEFAULT('{}'),
        [state]          SMALLINT         NOT NULL DEFAULT(0),
        [created_at]     DATETIMEOFFSET   NOT NULL DEFAULT(SYSDATETIMEOFFSET()),
        [updated_at]     DATETIMEOFFSET   NOT NULL DEFAULT(SYSDATETIMEOFFSET()),
        [processed_at]   DATETIMEOFFSET   NULL,
        [deliver_at]     DATETIMEOFFSET   NULL,       -- Scheduling: NULL = deliver ASAP
        [retry_count]    INT              NOT NULL DEFAULT(0),
        [owner_id]       UNIQUEIDENTIFIER NULL,
        [error]          NVARCHAR(MAX)    NULL,
        CONSTRAINT [PK_outbox_messages] PRIMARY KEY CLUSTERED ([id] ASC)
    )
END
GO

-- =============================================================================
-- 2. Idempotency (Inbox) table
-- =============================================================================
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE object_id = OBJECT_ID('[outbox].[idempotency]'))
BEGIN
    CREATE TABLE [outbox].[idempotency] (
        [message_id]   UNIQUEIDENTIFIER NOT NULL,
        [consumer_id]  NVARCHAR(255)    NOT NULL,
        [processed_at] DATETIMEOFFSET   NOT NULL,
        CONSTRAINT [PK_outbox_idempotency] PRIMARY KEY ([message_id], [consumer_id])
    )
END
GO

-- =============================================================================
-- 3. Dead Letter Queue table
-- =============================================================================
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE object_id = OBJECT_ID('[outbox].[dead_letters]'))
BEGIN
    CREATE TABLE [outbox].[dead_letters] (
        [id]                 UNIQUEIDENTIFIER NOT NULL,
        [original_message_id] UNIQUEIDENTIFIER NOT NULL,
        [type]               NVARCHAR(255)    NOT NULL,
        [payload]            NVARCHAR(MAX)    NOT NULL,
        [correlation_id]     NVARCHAR(255)    NULL,
        [causation_id]       NVARCHAR(255)    NULL,
        [headers_json]       NVARCHAR(MAX)    NOT NULL DEFAULT('{}'),
        [created_at]         DATETIMEOFFSET   NOT NULL,
        [dead_lettered_at]   DATETIMEOFFSET   NOT NULL DEFAULT(SYSDATETIMEOFFSET()),
        [retry_count]        INT              NOT NULL DEFAULT(0),
        [reason]             NVARCHAR(MAX)    NOT NULL,
        [last_error]         NVARCHAR(MAX)    NULL,
        CONSTRAINT [PK_outbox_dead_letters] PRIMARY KEY ([id])
    )
END
GO



-- =============================================================================
-- 5. Polling index — state + deliver_at + created_at covering id
-- Uses UPDLOCK + READPAST as SQL Server equivalent of SKIP LOCKED
-- =============================================================================
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_outbox_messages_pending' AND object_id = OBJECT_ID('[outbox].[messages]'))
BEGIN
    CREATE INDEX [IX_outbox_messages_pending]
        ON [outbox].[messages] ([state], [created_at] ASC)
        INCLUDE ([id], [deliver_at])
        WHERE [state] IN (0, 3)
END
GO

-- =============================================================================
-- 6. Cleanup index — age-based purge jobs
-- =============================================================================
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_outbox_messages_created_at' AND object_id = OBJECT_ID('[outbox].[messages]'))
BEGIN
    CREATE INDEX [IX_outbox_messages_created_at]
        ON [outbox].[messages] ([created_at] ASC)
END
-- =============================================================================
-- 01_Init_Outbox.sql
-- EricksonLopez.Outbox — SQL Server Initial Schema
-- Requires: SQL Server 2019+ (for JSON support and SKIP LOCKED equivalent)
-- =============================================================================

-- Create the schema if it doesn't exist
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'outbox')
BEGIN
    EXEC('CREATE SCHEMA [outbox]')
END
GO

-- =============================================================================
-- 1. Outbox Messages table
-- Column notes:
--   type          → message type alias (e.g. "order.created.v1"), NOT CLR type name
--   payload       → NVARCHAR(MAX) storing JSON (SQL Server 2022+ supports JSON type)
--   state         → 0=Pending, 1=InFlight, 2=Dispatched, 3=Failed, 4=DeadLettered
--   deliver_at    → NULL = immediate delivery; non-NULL = scheduled delivery
--   retry_count   → incremented on each dispatch failure
--   error         → last known error message, nullable
-- =============================================================================
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE object_id = OBJECT_ID('[outbox].[messages]'))
BEGIN
    CREATE TABLE [outbox].[messages] (
        [id]             UNIQUEIDENTIFIER NOT NULL,
        [type]           NVARCHAR(255)    NOT NULL,
        [payload]        NVARCHAR(MAX)    NOT NULL,   -- JSON payload
        [correlation_id] NVARCHAR(255)    NULL,
        [causation_id]   NVARCHAR(255)    NULL,
        [headers_json]   NVARCHAR(MAX)    NOT NULL DEFAULT('{}'),
        [state]          SMALLINT         NOT NULL DEFAULT(0),
        [created_at]     DATETIMEOFFSET   NOT NULL DEFAULT(SYSDATETIMEOFFSET()),
        [updated_at]     DATETIMEOFFSET   NOT NULL DEFAULT(SYSDATETIMEOFFSET()),
        [processed_at]   DATETIMEOFFSET   NULL,
        [deliver_at]     DATETIMEOFFSET   NULL,       -- Scheduling: NULL = deliver ASAP
        [retry_count]    INT              NOT NULL DEFAULT(0),
        [owner_id]       UNIQUEIDENTIFIER NULL,
        [error]          NVARCHAR(MAX)    NULL,
        CONSTRAINT [PK_outbox_messages] PRIMARY KEY CLUSTERED ([id] ASC)
    )
END
GO

-- =============================================================================
-- 2. Idempotency (Inbox) table
-- =============================================================================
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE object_id = OBJECT_ID('[outbox].[idempotency]'))
BEGIN
    CREATE TABLE [outbox].[idempotency] (
        [message_id]   UNIQUEIDENTIFIER NOT NULL,
        [consumer_id]  NVARCHAR(255)    NOT NULL,
        [processed_at] DATETIMEOFFSET   NOT NULL,
        CONSTRAINT [PK_outbox_idempotency] PRIMARY KEY ([message_id], [consumer_id])
    )
END
GO

-- =============================================================================
-- 3. Dead Letter Queue table
-- =============================================================================
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE object_id = OBJECT_ID('[outbox].[dead_letters]'))
BEGIN
    CREATE TABLE [outbox].[dead_letters] (
        [id]                 UNIQUEIDENTIFIER NOT NULL,
        [original_message_id] UNIQUEIDENTIFIER NOT NULL,
        [type]               NVARCHAR(255)    NOT NULL,
        [payload]            NVARCHAR(MAX)    NOT NULL,
        [correlation_id]     NVARCHAR(255)    NULL,
        [causation_id]       NVARCHAR(255)    NULL,
        [headers_json]       NVARCHAR(MAX)    NOT NULL DEFAULT('{}'),
        [created_at]         DATETIMEOFFSET   NOT NULL,
        [dead_lettered_at]   DATETIMEOFFSET   NOT NULL DEFAULT(SYSDATETIMEOFFSET()),
        [retry_count]        INT              NOT NULL DEFAULT(0),
        [reason]             NVARCHAR(MAX)    NOT NULL,
        [last_error]         NVARCHAR(MAX)    NULL,
        CONSTRAINT [PK_outbox_dead_letters] PRIMARY KEY ([id])
    )
END
GO



-- =============================================================================
-- 5. Polling index — state + deliver_at + created_at covering id
-- Uses UPDLOCK + READPAST as SQL Server equivalent of SKIP LOCKED
-- =============================================================================
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_outbox_messages_pending' AND object_id = OBJECT_ID('[outbox].[messages]'))
BEGIN
    CREATE INDEX [IX_outbox_messages_pending]
        ON [outbox].[messages] ([state], [created_at] ASC)
        INCLUDE ([id], [deliver_at])
        WHERE [state] IN (0, 3)
END
GO

-- =============================================================================
-- 6. Cleanup index — age-based purge jobs
-- =============================================================================
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_outbox_messages_created_at' AND object_id = OBJECT_ID('[outbox].[messages]'))
BEGIN
    CREATE INDEX [IX_outbox_messages_created_at]
        ON [outbox].[messages] ([created_at] ASC)
END
GO

-- =============================================================================
-- 7. Table-Valued Parameters (TVP)
-- Required for high-performance batch operations in SQL Server without GC pressure
-- =============================================================================
IF TYPE_ID('outbox.MessageKeysType') IS NULL
BEGIN
    CREATE TYPE [outbox].[MessageKeysType] AS TABLE (
        [Id] UNIQUEIDENTIFIER NOT NULL,
        [CreatedAt] DATETIMEOFFSET NOT NULL,
        PRIMARY KEY ([Id], [CreatedAt])
    );
END
GO

-- =============================================================================
-- 7. Operational index � state + updated_at (high volume environments)
-- =============================================================================
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_outbox_messages_updated_at' AND object_id = OBJECT_ID('[outbox].[messages]'))
BEGIN
    CREATE INDEX [IX_outbox_messages_updated_at]
        ON [outbox].[messages] ([state], [updated_at] ASC)
END
GO

