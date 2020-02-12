using Microsoft.EntityFrameworkCore.Migrations;

namespace FMBot.Data.Migrations
{
    public partial class addemotereactions : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EmoteReactions",
                table: "Guilds",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EmoteReactions",
                table: "Guilds");
        }
    }
}
