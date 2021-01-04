using Microsoft.EntityFrameworkCore.Migrations;

namespace FMBot.Persistence.EntityFrameWork.Migrations
{
    public partial class AddUserPlaycount : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "total_playcount",
                table: "users",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "bot",
                table: "guild_users",
                type: "boolean",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "total_playcount",
                table: "users");

            migrationBuilder.DropColumn(
                name: "bot",
                table: "guild_users");
        }
    }
}
