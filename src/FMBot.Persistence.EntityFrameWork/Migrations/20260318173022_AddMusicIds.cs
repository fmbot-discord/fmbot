using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FMBot.Persistence.EntityFrameWork.Migrations
{
    /// <inheritdoc />
    public partial class AddMusicIds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "track_id",
                table: "user_tracks",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "album_id",
                table: "user_plays",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "artist_id",
                table: "user_plays",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "track_id",
                table: "user_plays",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "artist_id",
                table: "user_artists",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "album_id",
                table: "user_albums",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "track_id",
                table: "user_tracks");

            migrationBuilder.DropColumn(
                name: "album_id",
                table: "user_plays");

            migrationBuilder.DropColumn(
                name: "artist_id",
                table: "user_plays");

            migrationBuilder.DropColumn(
                name: "track_id",
                table: "user_plays");

            migrationBuilder.DropColumn(
                name: "artist_id",
                table: "user_artists");

            migrationBuilder.DropColumn(
                name: "album_id",
                table: "user_albums");
        }
    }
}
