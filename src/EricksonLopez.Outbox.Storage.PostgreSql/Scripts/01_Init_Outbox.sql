-- =============================================================================
-- 01_Init_Outbox.sql
-- EricksonLopez.Outbox — Initial Schema
-- Requires: PostgreSQL 15+
-- =============================================================================

CREATE SCHEMA IF NOT EXISTS outbox;

-- =============================================================================
-- 1. Outbox Messages table
-- Column notes:
--   type          → message type alias (e.g. "order.created.v1"), NOT CLR type name
--   payload       → JSONB for query-ability, encryption hooks supported at app layer
--   state         → 0=Pending, 1=InFlight, 3=Failed, 4=DeadLettered
--                   NOTE: state=2 (Dispatched) is NEVER written to the database.
--                   Successfully dispatched rows are physically DELETED, not updated.
--                   Querying for state=2 will always return 0 rows.
--   deliver_at    → NULL = immediate delivery; non-NULL = scheduled delivery
--   retry_count   → incremented by RetryDispatcherInterceptor on each failure
--   error         → last known error message, nullable
-- =============================================================================
CREATE TABLE IF NOT EXISTS outbox.messages (
    id              UUID            NOT NULL,
    type            VARCHAR(255)    NOT NULL,
    payload         JSONB           NOT NULL,
    correlation_id  VARCHAR(255),
    causation_id    VARCHAR(255),
    headers_json    JSONB,
    state           SMALLINT        NOT NULL DEFAULT 0,
    created_at      TIMESTAMPTZ     NOT NULL DEFAULT NOW(),
    updated_at      TIMESTAMPTZ     NOT NULL DEFAULT NOW(),
    processed_at    TIMESTAMPTZ,
    deliver_at      TIMESTAMPTZ,                           -- Scheduling: NULL = deliver ASAP
    retry_count     INT             NOT NULL DEFAULT 0,
    owner_id        UUID,
    error           TEXT,
    PRIMARY KEY (id, created_at)                           -- Composite PK for partitioning
) PARTITION BY RANGE (created_at);

-- Default partition (catches overflow before named partitions are created)
CREATE TABLE IF NOT EXISTS outbox.messages_default
    PARTITION OF outbox.messages DEFAULT;

-- =============================================================================
-- 2. Idempotency (Inbox) table
-- =============================================================================
CREATE TABLE IF NOT EXISTS outbox.idempotency (
    message_id      UUID            NOT NULL,
    consumer_id     VARCHAR(255)    NOT NULL,
    processed_at    TIMESTAMPTZ     NOT NULL,
    PRIMARY KEY (message_id, consumer_id)
);

-- =============================================================================
-- 3. Dead Letter Queue table
-- =============================================================================
CREATE TABLE IF NOT EXISTS outbox.dead_letters (
    id                  UUID            NOT NULL,
    original_message_id UUID            NOT NULL,
    type                VARCHAR(255)    NOT NULL,
    payload             JSONB           NOT NULL,
    correlation_id      VARCHAR(255),
    causation_id        VARCHAR(255),
    headers_json        JSONB           NOT NULL DEFAULT '{}'::jsonb,
    created_at          TIMESTAMPTZ     NOT NULL,
    dead_lettered_at    TIMESTAMPTZ     NOT NULL DEFAULT NOW(),
    retry_count         INT             NOT NULL DEFAULT 0,
    error_reason        TEXT            NOT NULL,
    last_error          TEXT,
    PRIMARY KEY (id)
);



-- =============================================================================
-- 4. Autovacuum and storage tuning for messages (high-churn table)
-- =============================================================================
-- fillfactor=70: Reserve 30% of each heap page for HOT updates.
-- The outbox table undergoes frequent state transitions (0→1, 1→0 reclaim, 1→delete).
-- Without fillfactor headroom, each UPDATE must find a new page slot, causing page bloat
-- and HOT-chain breaks that degrade index access patterns.
-- fillfactor=70 is a conservative starting point; tune to 60-80 based on observed bloat.
ALTER TABLE outbox.messages_default SET (
    fillfactor                       = 70,
    autovacuum_vacuum_scale_factor   = 0.01,
    autovacuum_analyze_scale_factor  = 0.01,
    autovacuum_vacuum_cost_delay      = 2
);

-- =============================================================================
-- 5. LISTEN/NOTIFY for low-latency dispatch (eliminates polling gap)
-- =============================================================================
CREATE OR REPLACE FUNCTION outbox.notify_new_message()
RETURNS trigger AS $$
BEGIN
    -- Notifies all listening dispatcher instances that a new message is ready.
    -- The Dispatcher can switch from polling to LISTEN mode to reduce latency.
    PERFORM pg_notify('outbox_new_messages', '1');
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS outbox_new_messages_trigger ON outbox.messages;

-- FIX-03: Changed from FOR EACH STATEMENT to FOR EACH ROW.
--
-- Root cause: FOR EACH STATEMENT on a partitioned parent table does NOT propagate
-- automatically to child partition tables in PostgreSQL 15+. This meant that
-- LISTEN/NOTIFY was silently broken whenever table partitioning (03_Partitioning.sql)
-- was in use, because inserts go directly to the partition table, not the parent.
--
-- FOR EACH ROW triggers DO propagate to child partitions in PostgreSQL 15+,
-- making this the correct choice for partitioned outbox deployments.
--
-- SCRIPT EXECUTION ORDER:
--   Execute SQL scripts in this order for a fresh installation:
--     1. 01_Init_Outbox.sql  (this file)
--     2. 02_Indexes.sql      (indexes on parent table; propagate to existing partitions)
--     3. 03_Partitioning.sql (monthly partitions — run the DO block to apply autovacuum
--                             tuning and re-create triggers on pre-existing partitions)
-- If partitions already exist before running 02_Indexes.sql, run:
--   SELECT outbox.reindex_partitions(); -- (see 03_Partitioning.sql)
CREATE TRIGGER outbox_new_messages_trigger
    AFTER INSERT ON outbox.messages
    FOR EACH ROW EXECUTE FUNCTION outbox.notify_new_message();

