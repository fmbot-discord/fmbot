using Microsoft.EntityFrameworkCore.Migrations;

namespace FMBot.Persistence.EntityFrameWork.Migrations
{
    public partial class AddBlockedGuildUsers : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "crowns_eligible_threshold_days",
                table: "guilds",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "who_knows_eligible_threshold_days",
                table: "guilds",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "guild_blocked_users",
                columns: table => new
                {
                    guild_id = table.Column<int>(type: "integer", nullable: false),
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    blocked_from_crowns = table.Column<bool>(type: "boolean", nullable: false),
                    blocked_from_who_knows = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_guild_blocked_users", x => new { x.guild_id, x.user_id });
                    table.ForeignKey(
                        name: "fk_guild_blocked_users_guilds_guild_id",
                        column: x => x.guild_id,
                        principalTable: "guilds",
                        principalColumn: "guild_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_guild_blocked_users_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_guild_blocked_users_user_id",
                table: "guild_blocked_users",
                column: "user_id");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "guild_blocked_users");

            migrationBuilder.DropColumn(
                name: "crowns_eligible_threshold_days",
                table: "guilds");

            migrationBuilder.DropColumn(
                name: "who_knows_eligible_threshold_days",
                table: "guilds");
        }
    }
}
