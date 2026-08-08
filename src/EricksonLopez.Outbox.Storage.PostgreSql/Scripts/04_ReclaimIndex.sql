-- ==========================================
-- Reclaim Index for In-flight Messages
-- ==========================================
-- This index optimizes the ReclaimSql query which scans for messages stuck in state=1.
CREATE INDEX CONCURRENTLY IF NOT EXISTS outbox_messages_inflight_idx 
ON outbox.messages (updated_at ASC) 
WHERE state = 1;
