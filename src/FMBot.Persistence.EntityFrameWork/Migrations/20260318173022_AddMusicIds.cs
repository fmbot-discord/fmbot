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

            migrationBuilder.CreateIndex(
                name: "ix_artists_name_unique",
                table: "artists",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_albums_artist_name_name_unique",
                table: "albums",
                columns: new[] { "artist_name", "name" },
                unique: true);

            migrationBuilder.Sql(
                @"CREATE UNIQUE INDEX ix_tracks_artist_name_name_album_unique
                  ON tracks (artist_name, name, COALESCE(album_name, ''));");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_artists_name_unique",
                table: "artists");

            migrationBuilder.DropIndex(
                name: "ix_albums_artist_name_name_unique",
                table: "albums");

            migrationBuilder.DropIndex(
                name: "ix_tracks_artist_name_name_album_unique",
                table: "tracks");

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
