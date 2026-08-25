CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
);

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260825073218_Phase2_PostgreSqlFoundation') THEN
    CREATE TABLE audit_logs (
        id uuid NOT NULL,
        tenant_id uuid,
        user_id uuid,
        action character varying(64) NOT NULL,
        entity_type character varying(64) NOT NULL,
        entity_id character varying(128) NOT NULL,
        old_values jsonb,
        new_values jsonb,
        ip_address character varying(64),
        created_at timestamp with time zone NOT NULL,
        CONSTRAINT "PK_audit_logs" PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260825073218_Phase2_PostgreSqlFoundation') THEN
    CREATE TABLE tenants (
        id uuid NOT NULL,
        name character varying(128) NOT NULL,
        slug character varying(64) NOT NULL,
        is_active boolean NOT NULL,
        created_at timestamp with time zone NOT NULL,
        updated_at timestamp with time zone NOT NULL,
        CONSTRAINT "PK_tenants" PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260825073218_Phase2_PostgreSqlFoundation') THEN
    CREATE TABLE users (
        id uuid NOT NULL,
        email character varying(255) NOT NULL,
        password_hash character varying(255) NOT NULL,
        full_name character varying(128) NOT NULL,
        is_platform_admin boolean NOT NULL,
        email_confirmed boolean NOT NULL,
        mfa_enabled boolean NOT NULL,
        mfa_secret character varying(128),
        is_active boolean NOT NULL,
        created_at timestamp with time zone NOT NULL,
        updated_at timestamp with time zone NOT NULL,
        CONSTRAINT "PK_users" PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260825073218_Phase2_PostgreSqlFoundation') THEN
    CREATE TABLE webhook_endpoints (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        name character varying(128) NOT NULL,
        key_prefix character varying(16) NOT NULL,
        key_hash bytea NOT NULL,
        status character varying(32) NOT NULL,
        last_received_at timestamp with time zone,
        revoked_at timestamp with time zone,
        created_at timestamp with time zone NOT NULL,
        updated_at timestamp with time zone NOT NULL,
        CONSTRAINT "PK_webhook_endpoints" PRIMARY KEY (id),
        CONSTRAINT "FK_webhook_endpoints_tenants_tenant_id" FOREIGN KEY (tenant_id) REFERENCES tenants (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260825073218_Phase2_PostgreSqlFoundation') THEN
    CREATE TABLE tenant_memberships (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        user_id uuid NOT NULL,
        role character varying(32) NOT NULL,
        is_active boolean NOT NULL,
        created_at timestamp with time zone NOT NULL,
        updated_at timestamp with time zone NOT NULL,
        CONSTRAINT "PK_tenant_memberships" PRIMARY KEY (id),
        CONSTRAINT "FK_tenant_memberships_tenants_tenant_id" FOREIGN KEY (tenant_id) REFERENCES tenants (id) ON DELETE CASCADE,
        CONSTRAINT "FK_tenant_memberships_users_user_id" FOREIGN KEY (user_id) REFERENCES users (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260825073218_Phase2_PostgreSqlFoundation') THEN
    CREATE TABLE user_sessions (
        id uuid NOT NULL,
        user_id uuid NOT NULL,
        refresh_token_hash bytea NOT NULL,
        expires_at timestamp with time zone NOT NULL,
        revoked_at timestamp with time zone,
        ip_address character varying(64),
        user_agent character varying(255),
        created_at timestamp with time zone NOT NULL,
        CONSTRAINT "PK_user_sessions" PRIMARY KEY (id),
        CONSTRAINT "FK_user_sessions_users_user_id" FOREIGN KEY (user_id) REFERENCES users (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260825073218_Phase2_PostgreSqlFoundation') THEN
    CREATE TABLE messages (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        endpoint_id uuid NOT NULL,
        wamid character varying(255) NOT NULL,
        phone_number_id character varying(64),
        display_phone_number character varying(32),
        recipient_phone character varying(32),
        current_status character varying(32),
        status_rank smallint,
        last_status_timestamp timestamp with time zone,
        conversation_id character varying(128),
        conversation_origin_type character varying(64),
        conversation_expires_at timestamp with time zone,
        pricing_model character varying(32),
        pricing_category character varying(64),
        pricing_billable boolean,
        active_error_code character varying(32),
        active_error_title character varying(255),
        active_error_message text,
        active_error_details text,
        active_error_data jsonb,
        last_failure_code character varying(32),
        last_failure_timestamp timestamp with time zone,
        last_failure_reason character varying(255),
        biz_opaque_callback_data text,
        broadcast_id character varying(128),
        broadcast_name character varying(255),
        template_name character varying(128),
        created_at timestamp with time zone NOT NULL,
        updated_at timestamp with time zone NOT NULL,
        CONSTRAINT "PK_messages" PRIMARY KEY (id),
        CONSTRAINT "FK_messages_tenants_tenant_id" FOREIGN KEY (tenant_id) REFERENCES tenants (id) ON DELETE CASCADE,
        CONSTRAINT "FK_messages_webhook_endpoints_endpoint_id" FOREIGN KEY (endpoint_id) REFERENCES webhook_endpoints (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260825073218_Phase2_PostgreSqlFoundation') THEN
    CREATE TABLE webhook_inbox (
        id bigint GENERATED BY DEFAULT AS IDENTITY,
        tenant_id uuid NOT NULL,
        endpoint_id uuid NOT NULL,
        payload_raw jsonb NOT NULL,
        headers jsonb NOT NULL,
        ip_address character varying(64),
        status smallint NOT NULL,
        attempt_count smallint NOT NULL,
        last_error text,
        lock_token uuid,
        locked_by character varying(64),
        locked_until timestamp with time zone,
        next_attempt_at timestamp with time zone NOT NULL,
        created_at timestamp with time zone NOT NULL,
        processed_at timestamp with time zone,
        CONSTRAINT "PK_webhook_inbox" PRIMARY KEY (id),
        CONSTRAINT "FK_webhook_inbox_webhook_endpoints_endpoint_id" FOREIGN KEY (endpoint_id) REFERENCES webhook_endpoints (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260825073218_Phase2_PostgreSqlFoundation') THEN
    CREATE TABLE message_status_events (
        id uuid NOT NULL,
        message_id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        wamid character varying(255) NOT NULL,
        status character varying(32) NOT NULL,
        status_timestamp timestamp with time zone NOT NULL,
        event_fingerprint bytea NOT NULL,
        error_code character varying(32),
        error_title character varying(255),
        error_message text,
        error_details text,
        error_data jsonb,
        raw_event jsonb NOT NULL,
        created_at timestamp with time zone NOT NULL,
        CONSTRAINT "PK_message_status_events" PRIMARY KEY (id),
        CONSTRAINT "FK_message_status_events_messages_message_id" FOREIGN KEY (message_id) REFERENCES messages (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260825073218_Phase2_PostgreSqlFoundation') THEN
    CREATE INDEX ix_audit_logs_tenant_created ON audit_logs (tenant_id, created_at);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260825073218_Phase2_PostgreSqlFoundation') THEN
    CREATE UNIQUE INDEX ix_status_events_fingerprint ON message_status_events (event_fingerprint);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260825073218_Phase2_PostgreSqlFoundation') THEN
    CREATE INDEX ix_status_events_message_timestamp ON message_status_events (message_id, status_timestamp);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260825073218_Phase2_PostgreSqlFoundation') THEN
    CREATE INDEX ix_status_events_tenant_created ON message_status_events (tenant_id, created_at);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260825073218_Phase2_PostgreSqlFoundation') THEN
    CREATE INDEX "IX_messages_endpoint_id" ON messages (endpoint_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260825073218_Phase2_PostgreSqlFoundation') THEN
    CREATE INDEX ix_messages_tenant_created ON messages (tenant_id, created_at);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260825073218_Phase2_PostgreSqlFoundation') THEN
    CREATE INDEX ix_messages_tenant_recipient ON messages (tenant_id, recipient_phone);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260825073218_Phase2_PostgreSqlFoundation') THEN
    CREATE INDEX ix_messages_tenant_status_created ON messages (tenant_id, current_status, created_at);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260825073218_Phase2_PostgreSqlFoundation') THEN
    CREATE UNIQUE INDEX ix_messages_tenant_wamid ON messages (tenant_id, wamid);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260825073218_Phase2_PostgreSqlFoundation') THEN
    CREATE UNIQUE INDEX ix_tenant_memberships_tenant_user ON tenant_memberships (tenant_id, user_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260825073218_Phase2_PostgreSqlFoundation') THEN
    CREATE INDEX "IX_tenant_memberships_user_id" ON tenant_memberships (user_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260825073218_Phase2_PostgreSqlFoundation') THEN
    CREATE UNIQUE INDEX ix_tenants_slug ON tenants (slug);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260825073218_Phase2_PostgreSqlFoundation') THEN
    CREATE UNIQUE INDEX ix_user_sessions_refresh_token ON user_sessions (refresh_token_hash);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260825073218_Phase2_PostgreSqlFoundation') THEN
    CREATE INDEX "IX_user_sessions_user_id" ON user_sessions (user_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260825073218_Phase2_PostgreSqlFoundation') THEN
    CREATE UNIQUE INDEX ix_users_email ON users (email);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260825073218_Phase2_PostgreSqlFoundation') THEN
    CREATE UNIQUE INDEX ix_webhook_endpoints_key_hash ON webhook_endpoints (key_hash);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260825073218_Phase2_PostgreSqlFoundation') THEN
    CREATE INDEX ix_webhook_endpoints_tenant_created ON webhook_endpoints (tenant_id, created_at);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260825073218_Phase2_PostgreSqlFoundation') THEN
    CREATE INDEX "IX_webhook_inbox_endpoint_id" ON webhook_inbox (endpoint_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260825073218_Phase2_PostgreSqlFoundation') THEN
    CREATE INDEX ix_webhook_inbox_queue ON webhook_inbox (next_attempt_at, created_at) WHERE status IN (0, 1);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260825073218_Phase2_PostgreSqlFoundation') THEN
    CREATE INDEX ix_webhook_inbox_tenant_created ON webhook_inbox (tenant_id, created_at);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260825073218_Phase2_PostgreSqlFoundation') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260825073218_Phase2_PostgreSqlFoundation', '10.0.11');
    END IF;
END $EF$;
COMMIT;

