using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FMBot.Persistence.EntityFrameWork.Migrations
{
    /// <inheritdoc />
    public partial class ExpandJumble : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "album_name",
                table: "jumble_sessions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "artist_name",
                table: "jumble_sessions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<float>(
                name: "blur_level",
                table: "jumble_sessions",
                type: "real",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "album_name",
                table: "jumble_sessions");

            migrationBuilder.DropColumn(
                name: "artist_name",
                table: "jumble_sessions");

            migrationBuilder.DropColumn(
                name: "blur_level",
                table: "jumble_sessions");
        }
    }
}
