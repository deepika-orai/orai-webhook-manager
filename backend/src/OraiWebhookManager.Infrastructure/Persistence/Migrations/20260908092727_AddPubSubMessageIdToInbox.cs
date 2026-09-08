using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OraiWebhookManager.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Migration: AddPubSubMessageIdToInbox
    /// Purpose: Add nullable pubsub_message_id column and unique partial index with non-transactional DDL.
    /// Note: CREATE UNIQUE INDEX CONCURRENTLY and DROP INDEX CONCURRENTLY cannot run inside a transaction block.
    /// </summary>
    public partial class AddPubSubMessageIdToInbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "pubsub_message_id",
                table: "webhook_inbox",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.Sql(
                "CREATE UNIQUE INDEX CONCURRENTLY IF NOT EXISTS ix_webhook_inbox_pubsub_message_id ON webhook_inbox (pubsub_message_id) WHERE pubsub_message_id IS NOT NULL;",
                suppressTransaction: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "DROP INDEX CONCURRENTLY IF EXISTS ix_webhook_inbox_pubsub_message_id;",
                suppressTransaction: true);

            migrationBuilder.DropColumn(
                name: "pubsub_message_id",
                table: "webhook_inbox");
        }
    }
}
