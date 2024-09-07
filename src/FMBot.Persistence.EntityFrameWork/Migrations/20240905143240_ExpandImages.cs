using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FMBot.Persistence.EntityFrameWork.Migrations
{
    /// <inheritdoc />
    public partial class ExpandImages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "image_type",
                table: "artist_images",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "preview_frame_url",
                table: "artist_images",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "type",
                table: "albums",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "image_type",
                table: "album_images",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "preview_frame_url",
                table: "album_images",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "image_type",
                table: "artist_images");

            migrationBuilder.DropColumn(
                name: "preview_frame_url",
                table: "artist_images");

            migrationBuilder.DropColumn(
                name: "type",
                table: "albums");

            migrationBuilder.DropColumn(
                name: "image_type",
                table: "album_images");

            migrationBuilder.DropColumn(
                name: "preview_frame_url",
                table: "album_images");
        }
    }
}
