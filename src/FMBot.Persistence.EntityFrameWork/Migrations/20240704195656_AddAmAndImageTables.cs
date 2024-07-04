using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace FMBot.Persistence.EntityFrameWork.Migrations
{
    /// <inheritdoc />
    public partial class AddAmAndImageTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "apple_music_date",
                table: "tracks",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "apple_music_description",
                table: "tracks",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "apple_music_id",
                table: "tracks",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "apple_music_preview_url",
                table: "tracks",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "apple_music_short_description",
                table: "tracks",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "apple_music_tagline",
                table: "tracks",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "apple_music_url",
                table: "tracks",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "isrc",
                table: "tracks",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "spotify_preview_url",
                table: "tracks",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "apple_music_date",
                table: "artists",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "apple_music_id",
                table: "artists",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "apple_music_url",
                table: "artists",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "apple_music_date",
                table: "albums",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "apple_music_description",
                table: "albums",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "apple_music_id",
                table: "albums",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "apple_music_short_description",
                table: "albums",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "apple_music_tagline",
                table: "albums",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "apple_music_url",
                table: "albums",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "upc",
                table: "albums",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "album_images",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    album_id = table.Column<int>(type: "integer", nullable: false),
                    image_source = table.Column<int>(type: "integer", nullable: false),
                    url = table.Column<string>(type: "text", nullable: true),
                    last_updated = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    width = table.Column<int>(type: "integer", nullable: true),
                    height = table.Column<int>(type: "integer", nullable: true),
                    bg_color = table.Column<string>(type: "text", nullable: true),
                    text_color1 = table.Column<string>(type: "text", nullable: true),
                    text_color2 = table.Column<string>(type: "text", nullable: true),
                    text_color3 = table.Column<string>(type: "text", nullable: true),
                    text_color4 = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_album_images", x => x.id);
                    table.ForeignKey(
                        name: "fk_album_images_albums_album_id",
                        column: x => x.album_id,
                        principalTable: "albums",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "artist_images",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    artist_id = table.Column<int>(type: "integer", nullable: false),
                    image_source = table.Column<int>(type: "integer", nullable: false),
                    url = table.Column<string>(type: "text", nullable: true),
                    last_updated = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    width = table.Column<int>(type: "integer", nullable: true),
                    height = table.Column<int>(type: "integer", nullable: true),
                    bg_color = table.Column<string>(type: "text", nullable: true),
                    text_color1 = table.Column<string>(type: "text", nullable: true),
                    text_color2 = table.Column<string>(type: "text", nullable: true),
                    text_color3 = table.Column<string>(type: "text", nullable: true),
                    text_color4 = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_artist_images", x => x.id);
                    table.ForeignKey(
                        name: "fk_artist_images_artists_artist_id",
                        column: x => x.artist_id,
                        principalTable: "artists",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_album_images_album_id",
                table: "album_images",
                column: "album_id");

            migrationBuilder.CreateIndex(
                name: "ix_artist_images_artist_id",
                table: "artist_images",
                column: "artist_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "album_images");

            migrationBuilder.DropTable(
                name: "artist_images");

            migrationBuilder.DropColumn(
                name: "apple_music_date",
                table: "tracks");

            migrationBuilder.DropColumn(
                name: "apple_music_description",
                table: "tracks");

            migrationBuilder.DropColumn(
                name: "apple_music_id",
                table: "tracks");

            migrationBuilder.DropColumn(
                name: "apple_music_preview_url",
                table: "tracks");

            migrationBuilder.DropColumn(
                name: "apple_music_short_description",
                table: "tracks");

            migrationBuilder.DropColumn(
                name: "apple_music_tagline",
                table: "tracks");

            migrationBuilder.DropColumn(
                name: "apple_music_url",
                table: "tracks");

            migrationBuilder.DropColumn(
                name: "isrc",
                table: "tracks");

            migrationBuilder.DropColumn(
                name: "spotify_preview_url",
                table: "tracks");

            migrationBuilder.DropColumn(
                name: "apple_music_date",
                table: "artists");

            migrationBuilder.DropColumn(
                name: "apple_music_id",
                table: "artists");

            migrationBuilder.DropColumn(
                name: "apple_music_url",
                table: "artists");

            migrationBuilder.DropColumn(
                name: "apple_music_date",
                table: "albums");

            migrationBuilder.DropColumn(
                name: "apple_music_description",
                table: "albums");

            migrationBuilder.DropColumn(
                name: "apple_music_id",
                table: "albums");

            migrationBuilder.DropColumn(
                name: "apple_music_short_description",
                table: "albums");

            migrationBuilder.DropColumn(
                name: "apple_music_tagline",
                table: "albums");

            migrationBuilder.DropColumn(
                name: "apple_music_url",
                table: "albums");

            migrationBuilder.DropColumn(
                name: "upc",
                table: "albums");
        }
    }
}
