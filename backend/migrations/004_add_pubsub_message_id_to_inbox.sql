-- =====================================================================================
-- Migration 004: Add pubsub_message_id to webhook_inbox
-- Purpose: Add nullable pubsub_message_id column and unique partial index for idempotent
--          Pub/Sub insertion deduplication.
--
-- IMPORTANT CONCURRENCY & TRANSACTION NOTE:
-- CREATE UNIQUE INDEX CONCURRENTLY cannot run inside a multi-statement transaction block
-- (i.e. cannot be run inside START TRANSACTION / COMMIT). It must be executed as a
-- standalone non-transactional statement.
--
-- STEP 1: Add column (transactional or standalone)
-- STEP 2: Create index concurrently (standalone non-transactional)
--
-- MIGRATION TRACKING & DEPLOYMENT MECHANISM NOTE:
-- This script serves as a manual production fallback/reference.
-- EF Core migration (20260908092727_AddPubSubMessageIdToInbox) and this raw SQL script
-- represent two alternative deployment mechanisms for the same schema change.
-- Production deployment pipelines must execute EXACTLY ONE mechanism, NOT both.
-- If this raw script is executed manually, do not invent artificial __EFMigrationsHistory
-- entries unless your specific deployment workflow mandates it.
--
-- Index existence verification:
--   SELECT indexname, indexdef FROM pg_indexes
--   WHERE tablename = 'webhook_inbox' AND indexname = 'ix_webhook_inbox_pubsub_message_id';
-- =====================================================================================

-- Step 1: Add nullable column
ALTER TABLE webhook_inbox ADD COLUMN IF NOT EXISTS pubsub_message_id varchar(128);

-- Step 2: Create unique partial index concurrently (MUST run outside transaction)
CREATE UNIQUE INDEX CONCURRENTLY IF NOT EXISTS ix_webhook_inbox_pubsub_message_id
ON webhook_inbox (pubsub_message_id)
WHERE pubsub_message_id IS NOT NULL;
