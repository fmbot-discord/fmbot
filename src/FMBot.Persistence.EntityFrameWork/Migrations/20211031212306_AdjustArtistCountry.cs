using Microsoft.EntityFrameworkCore.Migrations;

namespace FMBot.Persistence.EntityFrameWork.Migrations
{
    public partial class AdjustArtistCountry : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "country",
                table: "artists",
                newName: "location");

            migrationBuilder.AddColumn<string>(
                name: "country_code",
                table: "artists",
                type: "text",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "country_code",
                table: "artists");

            migrationBuilder.RenameColumn(
                name: "location",
                table: "artists",
                newName: "country");
        }
    }
}
