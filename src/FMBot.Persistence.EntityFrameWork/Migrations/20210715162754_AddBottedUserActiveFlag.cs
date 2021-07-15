using Microsoft.EntityFrameworkCore.Migrations;

namespace FMBot.Persistence.EntityFrameWork.Migrations
{
    public partial class AddBottedUserActiveFlag : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "aliases",
                table: "artists");

            migrationBuilder.AddColumn<bool>(
                name: "ban_active",
                table: "botted_users",
                type: "boolean",
                nullable: false,
                defaultValue: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ban_active",
                table: "botted_users");

            migrationBuilder.AddColumn<string>(
                name: "aliases",
                table: "artists",
                type: "text",
                nullable: true);
        }
    }
}
