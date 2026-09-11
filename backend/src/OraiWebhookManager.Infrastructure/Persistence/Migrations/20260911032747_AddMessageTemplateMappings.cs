using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OraiWebhookManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMessageTemplateMappings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "message_template_mappings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    endpoint_id = table.Column<Guid>(type: "uuid", nullable: false),
                    wamid = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    recipient_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    template_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    template_namespace = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    template_language = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    broadcast_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    broadcast_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_message_template_mappings", x => x.id);
                    table.ForeignKey(
                        name: "FK_message_template_mappings_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_message_template_mappings_webhook_endpoints_endpoint_id",
                        column: x => x.endpoint_id,
                        principalTable: "webhook_endpoints",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_template_mappings_endpoint_created",
                table: "message_template_mappings",
                columns: new[] { "endpoint_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_template_mappings_tenant_created",
                table: "message_template_mappings",
                columns: new[] { "tenant_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_template_mappings_tenant_wamid",
                table: "message_template_mappings",
                columns: new[] { "tenant_id", "wamid" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "message_template_mappings");
        }
    }
}
