using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FMBot.Persistence.EntityFrameWork.Migrations
{
    /// <inheritdoc />
    public partial class AddFmSettingsAndMode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "who_knows_mode",
                table: "users",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "user_fm_settings",
                columns: table => new
                {
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    embed_type = table.Column<int>(type: "integer", nullable: false),
                    small_text_type = table.Column<int>(type: "integer", nullable: true),
                    accent_color = table.Column<int>(type: "integer", nullable: true),
                    custom_color = table.Column<string>(type: "text", nullable: true),
                    buttons = table.Column<long>(type: "bigint", nullable: true),
                    private_button_response = table.Column<bool>(type: "boolean", nullable: true),
                    footer_options = table.Column<long>(type: "bigint", nullable: false),
                    modified = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_fm_settings", x => x.user_id);
                    table.ForeignKey(
                        name: "fk_user_fm_settings_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "user_fm_settings");

            migrationBuilder.DropColumn(
                name: "who_knows_mode",
                table: "users");
        }
    }
}
