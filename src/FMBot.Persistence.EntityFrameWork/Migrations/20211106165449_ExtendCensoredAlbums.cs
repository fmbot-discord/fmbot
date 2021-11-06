using Microsoft.EntityFrameworkCore.Migrations;

namespace FMBot.Persistence.EntityFrameWork.Migrations
{
    public partial class ExtendCensoredAlbums : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "alternative_cover_url",
                table: "censored_music",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "times_censored",
                table: "censored_music",
                type: "integer",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "alternative_cover_url",
                table: "censored_music");

            migrationBuilder.DropColumn(
                name: "times_censored",
                table: "censored_music");
        }
    }
}
