using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OraiWebhookManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase4_AuthSuperAdminOnboarding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "auth_version",
                table: "users",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<bool>(
                name: "must_change_password",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "auth_version",
                table: "users");

            migrationBuilder.DropColumn(
                name: "must_change_password",
                table: "users");
        }
    }
}
