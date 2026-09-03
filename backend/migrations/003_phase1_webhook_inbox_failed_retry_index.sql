-- =====================================================================================
-- Migration 003: Phase 1 Webhook Inbox Failed Retry Index
-- Purpose: Add partial index for status = 3 (Failed) retry queue polling without table locks.
--
-- IMPORTANT CONCURRENCY & TRANSACTION NOTE:
-- CREATE INDEX CONCURRENTLY cannot run inside a multi-statement transaction block
-- (i.e. cannot be run inside START TRANSACTION / COMMIT). It must be executed as a
-- standalone non-transactional statement.
--
-- MIGRATION TRACKING NOTE:
-- This is a non-transactional maintenance DDL migration. In environments using
-- __EFMigrationsHistory or deployment pipelines, verify index existence via:
--   SELECT indexname, indexdef FROM pg_indexes
--   WHERE tablename = 'webhook_inbox' AND indexname = 'ix_webhook_inbox_failed_retry';
-- =====================================================================================

CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_webhook_inbox_failed_retry
ON webhook_inbox (next_attempt_at, created_at)
WHERE status = 3;
