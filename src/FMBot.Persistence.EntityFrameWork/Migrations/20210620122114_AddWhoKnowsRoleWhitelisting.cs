using Microsoft.EntityFrameworkCore.Migrations;

namespace FMBot.Persistence.EntityFrameWork.Migrations
{
    public partial class AddWhoKnowsRoleWhitelisting : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "who_knows_whitelist_role_id",
                table: "guilds",
                type: "numeric(20,0)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "who_knows_whitelisted",
                table: "guild_users",
                type: "boolean",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "who_knows_whitelist_role_id",
                table: "guilds");

            migrationBuilder.DropColumn(
                name: "who_knows_whitelisted",
                table: "guild_users");
        }
    }
}
