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
