-- SOURCE: 01_Init_Outbox.sql
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



-- SOURCE: 02_Indexes.sql
-- =============================================================================
-- 02_Indexes.sql
-- PostgreSQL Outbox — Index Strategy
-- =============================================================================
-- Index design rationale:
--
--   1. PENDING_STATE_IDX  — Covering index for the polling query.
--      The dispatcher always filters by state=0 and orders by created_at ASC.
--      Including `id` makes the index covering, allowing index-only scans.
--
--   2. CREATED_AT_IDX     — Supports archiving/TTL jobs that filter by age.
--
--   3. CORRELATION_IDX    — Optional. Supports tracing queries from external systems.
--      CREATE with CONCURRENTLY to avoid locking in production.
--
--   4. IDEMPOTENCY_PK     — Already a PK (composite: message_id + consumer_id).
--      No additional index needed.
--
--   Note: We deliberately avoid indexing the `payload` JSONB column at the base level
--   because GIN indexes add significant write overhead and payload querying is not
--   a core dispatcher use case.
--
-- Storage tuning (fillfactor):
--   The outbox table has a high UPDATE/DELETE ratio (state transitions + Delete-on-Dispatch).
--   Setting fillfactor=70 on the table reserves 30% of each heap page for HOT updates,
--   reducing page splits and write amplification. This is already set in 01_Init_Outbox.sql
--   via ALTER TABLE. Per-index fillfactor should be set to the same value for B-tree indexes
--   that are frequently updated (particularly on partitioned child tables):
--     ALTER INDEX outbox_messages_pending_immediate_idx SET (fillfactor = 70);
--
-- =============================================================================

-- 1a. Primary polling index for immediate messages: state ASC, created_at ASC (covering id)
CREATE INDEX CONCURRENTLY IF NOT EXISTS outbox_messages_pending_immediate_idx
    ON outbox.messages (state, created_at ASC)
    INCLUDE (id)
    WHERE state IN (0, 3) AND deliver_at IS NULL;

-- 1b. Polling index for scheduled messages: state ASC, deliver_at ASC, created_at ASC
CREATE INDEX CONCURRENTLY IF NOT EXISTS outbox_messages_pending_scheduled_idx
    ON outbox.messages (state, deliver_at ASC, created_at ASC)
    WHERE state IN (0, 3) AND deliver_at IS NOT NULL;

-- 2. Archive / cleanup index: age-based purge jobs
CREATE INDEX CONCURRENTLY IF NOT EXISTS outbox_messages_created_at_idx
    ON outbox.messages (created_at ASC);

-- 3. Correlation tracing index (optional — comment out if not needed)
CREATE INDEX CONCURRENTLY IF NOT EXISTS outbox_messages_correlation_idx
    ON outbox.messages (correlation_id)
    WHERE correlation_id IS NOT NULL;

-- 4. Dead-letter index: efficiently query failed messages
CREATE INDEX CONCURRENTLY IF NOT EXISTS outbox_messages_failed_idx
    ON outbox.messages (state, created_at ASC)
    WHERE state IN (3, 4);  -- state=3: failed, state=4: dead-lettered

-- 5. Idempotency cleanup index: purge old records by processed_at
CREATE INDEX CONCURRENTLY IF NOT EXISTS outbox_idempotency_purge_idx
    ON outbox.idempotency (processed_at ASC);

-- 6. Stale message reclaim index: partial index for recovering InFlight (state=1) messages
CREATE INDEX CONCURRENTLY IF NOT EXISTS outbox_messages_reclaim_idx
    ON outbox.messages (state, updated_at ASC)
    WHERE state = 1;

-- =============================================================================
-- Scheduling edge case — deliver_at with MaxMessageAge:
--
--   If a message is stored with deliver_at = NOW() + 1 year
--   AND your outbox.MaxMessageAge is set to 30 days,
--   the message will be silently excluded from polling (created_at guard in FetchPendingAsync)
--   before the deliver_at timestamp is reached. The message will NOT be processed
--   and will NOT be automatically dead-lettered.
--
--   Mitigation: ensure MaxMessageAge > maximum deliver_at offset you use for scheduling.
--   Example: if you schedule messages up to 7 days in the future, set MaxMessageAge >= 8 days.
-- =============================================================================



-- SOURCE: 03_Partitioning.sql
-- =============================================================================
-- 03_Partitioning.sql
-- PostgreSQL Outbox — Range Partitioning + Archiving Strategy
-- =============================================================================
-- Partitioning rationale:
--
--   High-volume systems generate millions of outbox rows/day. Without partitioning,
--   the outbox table bloats indefinitely and VACUUM struggles to reclaim space.
--
--   Strategy: Range partition by `created_at` (monthly).
--     - Old partitions are detached and archived without affecting live queries.
--     - VACUUM / autovacuum operates per-partition (smaller segments = faster).
--     - The polling query (state=0, ORDER BY created_at) hits at most 1-2 partitions.
--
--   Trade-off vs. UNLOGGED:
--     UNLOGGED is faster but data is lost on crash. We use LOGGED partitioned tables
--     because outbox durability is the entire point of the pattern.
--
--   TTL / Archiving:
--     Processed partitions (all state=2) can be DETACHED and moved to cold storage
--     (e.g., S3 via pg_archivecleanup or pg_dump per-partition) then DROPped.
-- =============================================================================

-- Drop existing non-partitioned table if present (run only during initial migration)
-- NOTE: Only execute this block on a fresh installation. For live migrations, use pg_repack.
-- ALTER TABLE outbox.messages RENAME TO messages_legacy;

-- Partitioned parent table
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
    deliver_at      TIMESTAMPTZ,
    retry_count     INT             NOT NULL DEFAULT 0,
    owner_id        UUID,
    error           TEXT,
    PRIMARY KEY (id, created_at)
) PARTITION BY RANGE (created_at);

-- Monthly partitions (pre-create 3 months ahead in production via a cron job)
CREATE TABLE IF NOT EXISTS outbox.messages_y2026_m01
    PARTITION OF outbox.messages
    FOR VALUES FROM ('2026-01-01') TO ('2026-02-01');

CREATE TABLE IF NOT EXISTS outbox.messages_y2026_m02
    PARTITION OF outbox.messages
    FOR VALUES FROM ('2026-02-01') TO ('2026-03-01');

CREATE TABLE IF NOT EXISTS outbox.messages_y2026_m03
    PARTITION OF outbox.messages
    FOR VALUES FROM ('2026-03-01') TO ('2026-04-01');

CREATE TABLE IF NOT EXISTS outbox.messages_y2026_m04
    PARTITION OF outbox.messages
    FOR VALUES FROM ('2026-04-01') TO ('2026-05-01');

CREATE TABLE IF NOT EXISTS outbox.messages_y2026_m05
    PARTITION OF outbox.messages
    FOR VALUES FROM ('2026-05-01') TO ('2026-06-01');

CREATE TABLE IF NOT EXISTS outbox.messages_y2026_m06
    PARTITION OF outbox.messages
    FOR VALUES FROM ('2026-06-01') TO ('2026-07-01');

CREATE TABLE IF NOT EXISTS outbox.messages_y2026_m07
    PARTITION OF outbox.messages
    FOR VALUES FROM ('2026-07-01') TO ('2026-08-01');

CREATE TABLE IF NOT EXISTS outbox.messages_y2026_m08
    PARTITION OF outbox.messages
    FOR VALUES FROM ('2026-08-01') TO ('2026-09-01');

CREATE TABLE IF NOT EXISTS outbox.messages_y2026_m09
    PARTITION OF outbox.messages
    FOR VALUES FROM ('2026-09-01') TO ('2026-10-01');

CREATE TABLE IF NOT EXISTS outbox.messages_y2026_m10
    PARTITION OF outbox.messages
    FOR VALUES FROM ('2026-10-01') TO ('2026-11-01');

CREATE TABLE IF NOT EXISTS outbox.messages_y2026_m11
    PARTITION OF outbox.messages
    FOR VALUES FROM ('2026-11-01') TO ('2026-12-01');

CREATE TABLE IF NOT EXISTS outbox.messages_y2026_m12
    PARTITION OF outbox.messages
    FOR VALUES FROM ('2026-12-01') TO ('2027-01-01');

-- Default partition catches any overflow (remove after all valid partitions exist)
CREATE TABLE IF NOT EXISTS outbox.messages_default
    PARTITION OF outbox.messages DEFAULT;

-- =============================================================================
-- Archiving procedure (call monthly via pg_cron or an external job)
-- =============================================================================
CREATE OR REPLACE PROCEDURE outbox.archive_old_partition(p_partition_name TEXT)
LANGUAGE plpgsql AS $$
BEGIN
    -- Detach old partition from the parent (zero-downtime)
    EXECUTE format('ALTER TABLE outbox.messages DETACH PARTITION outbox.%I CONCURRENTLY', p_partition_name);

    -- Optionally: COPY old data to a cold storage archive table / foreign table
    -- Then: DROP TABLE outbox.<partition_name>;

    RAISE NOTICE 'Partition % detached and ready for archiving.', p_partition_name;
END;
$$;

-- =============================================================================
-- Autovacuum tuning per partition (applied to each partition individually)
-- P2-FIX: Also apply fillfactor=70 to all monthly partitions.
-- Previously, fillfactor was only set on messages_default; the monthly partitions
-- did not inherit it. Without fillfactor, UPDATE operations on InFlight messages
-- (state=0 → state=1) always create new tuple versions in full pages, causing
-- immediate VACUUM fragmentation. fillfactor=70 reserves 30% of each page for
-- in-place HOT updates, significantly reducing VACUUM pressure.
-- =============================================================================
DO $$
DECLARE
    part TEXT;
BEGIN
    FOR part IN
        SELECT tablename
        FROM   pg_tables
        WHERE  schemaname = 'outbox'
          AND  tablename LIKE 'messages_y%'
    LOOP
        EXECUTE format(
            'ALTER TABLE outbox.%I SET (
                autovacuum_vacuum_scale_factor   = 0.01,
                autovacuum_analyze_scale_factor  = 0.01,
                autovacuum_vacuum_cost_delay      = 2,
                fillfactor                        = 70
            )', part);

        EXECUTE format(
            'DROP TRIGGER IF EXISTS outbox_new_messages_trigger ON outbox.%I;
             CREATE TRIGGER outbox_new_messages_trigger
                 AFTER INSERT ON outbox.%I
                 FOR EACH ROW EXECUTE FUNCTION outbox.notify_new_message();', part, part);
    END LOOP;
END;
$$;

-- =============================================================================
-- Helper function: outbox.apply_partition_settings()
-- Call this immediately after creating a new monthly partition.
--
-- Usage:
--   CREATE TABLE outbox.messages_y2027_m01 ...
--   SELECT outbox.apply_partition_settings('messages_y2027_m01');
-- =============================================================================
CREATE OR REPLACE FUNCTION outbox.apply_partition_settings(p_partition_name TEXT)
RETURNS void
LANGUAGE plpgsql AS $$
BEGIN
    -- Apply fillfactor and autovacuum tuning identical to existing partitions.
    EXECUTE format(
        'ALTER TABLE outbox.%I SET (
            autovacuum_vacuum_scale_factor   = 0.01,
            autovacuum_analyze_scale_factor  = 0.01,
            autovacuum_vacuum_cost_delay      = 2,
            fillfactor                        = 70
        )', p_partition_name);

    -- Create the LISTEN/NOTIFY trigger on the new partition.
    EXECUTE format(
        'DROP TRIGGER IF EXISTS outbox_new_messages_trigger ON outbox.%I;
         CREATE TRIGGER outbox_new_messages_trigger
             AFTER INSERT ON outbox.%I
             FOR EACH ROW EXECUTE FUNCTION outbox.notify_new_message();',
        p_partition_name, p_partition_name);

    RAISE NOTICE 'Applied partition settings to outbox.%', p_partition_name;
END;
$$;


-- SOURCE: 04_ReclaimIndex.sql
-- ==========================================
-- Reclaim Index for In-flight Messages
-- ==========================================
-- This index optimizes the ReclaimSql query which scans for messages stuck in state=1.
CREATE INDEX CONCURRENTLY IF NOT EXISTS outbox_messages_inflight_idx 
ON outbox.messages (updated_at ASC) 
WHERE state = 1;



