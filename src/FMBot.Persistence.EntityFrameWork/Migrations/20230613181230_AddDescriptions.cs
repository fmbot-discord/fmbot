using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FMBot.Persistence.EntityFrameWork.Migrations
{
    /// <inheritdoc />
    public partial class AddDescriptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "last_fm_description",
                table: "tracks",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "last_fm_description",
                table: "artists",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "last_fm_description",
                table: "albums",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "last_fm_description",
                table: "tracks");

            migrationBuilder.DropColumn(
                name: "last_fm_description",
                table: "artists");

            migrationBuilder.DropColumn(
                name: "last_fm_description",
                table: "albums");
        }
    }
}
