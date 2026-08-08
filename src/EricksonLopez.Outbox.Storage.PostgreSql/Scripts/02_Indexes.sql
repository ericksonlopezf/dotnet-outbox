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

-- 6. Operational index: state + updated_at (high volume environments)
CREATE INDEX CONCURRENTLY IF NOT EXISTS outbox_messages_updated_at_idx
    ON outbox.messages (state, updated_at ASC);

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

