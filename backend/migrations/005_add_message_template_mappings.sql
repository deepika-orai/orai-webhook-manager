-- =====================================================================================
-- Migration 005: Add message_template_mappings table
-- Purpose: Create message_template_mappings table for template metadata mapping,
--          correlating incoming WhatsApp status webhooks with broadcast templates.
--
-- EF Core Migration: 20260911032747_AddMessageTemplateMappings
--
-- MIGRATION TRACKING & DEPLOYMENT MECHANISM NOTE:
-- This script serves as a deployment-ready idempotent SQL script.
-- If applied via SQL tooling (psql/Azure CLI), it records the migration entry
-- in __EFMigrationsHistory so EF Core state stays synchronized.
-- =====================================================================================

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260911032747_AddMessageTemplateMappings') THEN
    CREATE TABLE IF NOT EXISTS message_template_mappings (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        endpoint_id uuid NOT NULL,
        wamid character varying(255) NOT NULL,
        recipient_id character varying(64) NOT NULL,
        template_name character varying(128) NOT NULL,
        template_namespace character varying(128),
        template_language character varying(32) NOT NULL,
        sent_at timestamp with time zone NOT NULL,
        broadcast_id character varying(128),
        broadcast_name character varying(255),
        created_at timestamp with time zone NOT NULL,
        updated_at timestamp with time zone NOT NULL,
        CONSTRAINT "PK_message_template_mappings" PRIMARY KEY (id),
        CONSTRAINT "FK_message_template_mappings_tenants_tenant_id" FOREIGN KEY (tenant_id) REFERENCES tenants (id) ON DELETE CASCADE,
        CONSTRAINT "FK_message_template_mappings_webhook_endpoints_endpoint_id" FOREIGN KEY (endpoint_id) REFERENCES webhook_endpoints (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260911032747_AddMessageTemplateMappings') THEN
    CREATE INDEX IF NOT EXISTS ix_template_mappings_endpoint_created ON message_template_mappings (endpoint_id, created_at);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260911032747_AddMessageTemplateMappings') THEN
    CREATE INDEX IF NOT EXISTS ix_template_mappings_tenant_created ON message_template_mappings (tenant_id, created_at);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260911032747_AddMessageTemplateMappings') THEN
    CREATE UNIQUE INDEX IF NOT EXISTS ix_template_mappings_tenant_wamid ON message_template_mappings (tenant_id, wamid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260911032747_AddMessageTemplateMappings') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260911032747_AddMessageTemplateMappings', '10.0.11');
    END IF;
END $EF$;

COMMIT;
