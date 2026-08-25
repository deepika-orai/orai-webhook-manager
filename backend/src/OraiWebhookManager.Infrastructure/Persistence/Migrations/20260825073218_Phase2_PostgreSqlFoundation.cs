using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace OraiWebhookManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase2_PostgreSqlFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "audit_logs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    action = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    entity_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    entity_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    old_values = table.Column<string>(type: "jsonb", nullable: true),
                    new_values = table.Column<string>(type: "jsonb", nullable: true),
                    ip_address = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_logs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "tenants",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    slug = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tenants", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    password_hash = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    full_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    is_platform_admin = table.Column<bool>(type: "boolean", nullable: false),
                    email_confirmed = table.Column<bool>(type: "boolean", nullable: false),
                    mfa_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    mfa_secret = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_users", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "webhook_endpoints",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    key_prefix = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    key_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    last_received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_webhook_endpoints", x => x.id);
                    table.ForeignKey(
                        name: "FK_webhook_endpoints_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tenant_memberships",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tenant_memberships", x => x.id);
                    table.ForeignKey(
                        name: "FK_tenant_memberships_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_tenant_memberships_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_sessions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    refresh_token_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ip_address = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    user_agent = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_sessions", x => x.id);
                    table.ForeignKey(
                        name: "FK_user_sessions_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "messages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    endpoint_id = table.Column<Guid>(type: "uuid", nullable: false),
                    wamid = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    phone_number_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    display_phone_number = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    recipient_phone = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    current_status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    status_rank = table.Column<short>(type: "smallint", nullable: true),
                    last_status_timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    conversation_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    conversation_origin_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    conversation_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    pricing_model = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    pricing_category = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    pricing_billable = table.Column<bool>(type: "boolean", nullable: true),
                    active_error_code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    active_error_title = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    active_error_message = table.Column<string>(type: "text", nullable: true),
                    active_error_details = table.Column<string>(type: "text", nullable: true),
                    active_error_data = table.Column<string>(type: "jsonb", nullable: true),
                    last_failure_code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    last_failure_timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_failure_reason = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    biz_opaque_callback_data = table.Column<string>(type: "text", nullable: true),
                    broadcast_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    broadcast_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    template_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_messages", x => x.id);
                    table.ForeignKey(
                        name: "FK_messages_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_messages_webhook_endpoints_endpoint_id",
                        column: x => x.endpoint_id,
                        principalTable: "webhook_endpoints",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "webhook_inbox",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    endpoint_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payload_raw = table.Column<string>(type: "jsonb", nullable: false),
                    headers = table.Column<string>(type: "jsonb", nullable: false),
                    ip_address = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    status = table.Column<short>(type: "smallint", nullable: false),
                    attempt_count = table.Column<short>(type: "smallint", nullable: false),
                    last_error = table.Column<string>(type: "text", nullable: true),
                    lock_token = table.Column<Guid>(type: "uuid", nullable: true),
                    locked_by = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    locked_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    next_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_webhook_inbox", x => x.id);
                    table.ForeignKey(
                        name: "FK_webhook_inbox_webhook_endpoints_endpoint_id",
                        column: x => x.endpoint_id,
                        principalTable: "webhook_endpoints",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "message_status_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    message_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    wamid = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    status_timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    event_fingerprint = table.Column<byte[]>(type: "bytea", nullable: false),
                    error_code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    error_title = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    error_message = table.Column<string>(type: "text", nullable: true),
                    error_details = table.Column<string>(type: "text", nullable: true),
                    error_data = table.Column<string>(type: "jsonb", nullable: true),
                    raw_event = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_message_status_events", x => x.id);
                    table.ForeignKey(
                        name: "FK_message_status_events_messages_message_id",
                        column: x => x.message_id,
                        principalTable: "messages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_tenant_created",
                table: "audit_logs",
                columns: new[] { "tenant_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_status_events_fingerprint",
                table: "message_status_events",
                column: "event_fingerprint",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_status_events_message_timestamp",
                table: "message_status_events",
                columns: new[] { "message_id", "status_timestamp" });

            migrationBuilder.CreateIndex(
                name: "ix_status_events_tenant_created",
                table: "message_status_events",
                columns: new[] { "tenant_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_messages_endpoint_id",
                table: "messages",
                column: "endpoint_id");

            migrationBuilder.CreateIndex(
                name: "ix_messages_tenant_created",
                table: "messages",
                columns: new[] { "tenant_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_messages_tenant_recipient",
                table: "messages",
                columns: new[] { "tenant_id", "recipient_phone" });

            migrationBuilder.CreateIndex(
                name: "ix_messages_tenant_status_created",
                table: "messages",
                columns: new[] { "tenant_id", "current_status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_messages_tenant_wamid",
                table: "messages",
                columns: new[] { "tenant_id", "wamid" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tenant_memberships_tenant_user",
                table: "tenant_memberships",
                columns: new[] { "tenant_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tenant_memberships_user_id",
                table: "tenant_memberships",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_tenants_slug",
                table: "tenants",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_user_sessions_refresh_token",
                table: "user_sessions",
                column: "refresh_token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_user_sessions_user_id",
                table: "user_sessions",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_users_email",
                table: "users",
                column: "email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_webhook_endpoints_key_hash",
                table: "webhook_endpoints",
                column: "key_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_webhook_endpoints_tenant_created",
                table: "webhook_endpoints",
                columns: new[] { "tenant_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_webhook_inbox_endpoint_id",
                table: "webhook_inbox",
                column: "endpoint_id");

            migrationBuilder.CreateIndex(
                name: "ix_webhook_inbox_queue",
                table: "webhook_inbox",
                columns: new[] { "next_attempt_at", "created_at" },
                filter: "status IN (0, 1)");

            migrationBuilder.CreateIndex(
                name: "ix_webhook_inbox_tenant_created",
                table: "webhook_inbox",
                columns: new[] { "tenant_id", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_logs");

            migrationBuilder.DropTable(
                name: "message_status_events");

            migrationBuilder.DropTable(
                name: "tenant_memberships");

            migrationBuilder.DropTable(
                name: "user_sessions");

            migrationBuilder.DropTable(
                name: "webhook_inbox");

            migrationBuilder.DropTable(
                name: "messages");

            migrationBuilder.DropTable(
                name: "users");

            migrationBuilder.DropTable(
                name: "webhook_endpoints");

            migrationBuilder.DropTable(
                name: "tenants");
        }
    }
}
