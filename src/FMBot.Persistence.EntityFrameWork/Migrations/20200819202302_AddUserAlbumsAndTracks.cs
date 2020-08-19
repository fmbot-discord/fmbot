using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

namespace FMBot.Persistence.EntityFrameWork.Migrations
{
    public partial class AddUserAlbumsAndTracks : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "pk_user_artists",
                table: "user_artists");

            migrationBuilder.DropColumn(
                name: "artist_id",
                table: "user_artists");

            migrationBuilder.AddColumn<DateTime>(
                name: "last_updated",
                table: "users",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "user_artist_id",
                table: "user_artists",
                nullable: false,
                defaultValue: 0)
                .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

            migrationBuilder.AddColumn<int>(
                name: "album_id",
                table: "tracks",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "popularity",
                table: "tracks",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "genres",
                table: "artists",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "popularity",
                table: "artists",
                nullable: true);

            migrationBuilder.AddPrimaryKey(
                name: "pk_user_artists",
                table: "user_artists",
                column: "user_artist_id");

            migrationBuilder.CreateTable(
                name: "album",
                columns: table => new
                {
                    id = table.Column<int>(nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(nullable: true),
                    artist_name = table.Column<string>(nullable: true),
                    last_fm_url = table.Column<string>(nullable: true),
                    mbid = table.Column<Guid>(nullable: true),
                    spotify_image_url = table.Column<string>(nullable: true),
                    spotify_image_date = table.Column<DateTime>(nullable: true),
                    spotify_id = table.Column<string>(nullable: true),
                    popularity = table.Column<int>(nullable: true),
                    label = table.Column<string>(nullable: true),
                    release_date = table.Column<DateTime>(nullable: true),
                    artist_id = table.Column<int>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_album", x => x.id);
                    table.ForeignKey(
                        name: "fk_album_artists_artist_id",
                        column: x => x.artist_id,
                        principalTable: "artists",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_album",
                columns: table => new
                {
                    user_album_id = table.Column<int>(nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<int>(nullable: false),
                    name = table.Column<string>(nullable: true),
                    artist_name = table.Column<string>(nullable: true),
                    playcount = table.Column<int>(nullable: false),
                    last_updated = table.Column<DateTime>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_album", x => x.user_album_id);
                    table.ForeignKey(
                        name: "fk_user_album_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_track",
                columns: table => new
                {
                    user_track_id = table.Column<int>(nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<int>(nullable: false),
                    name = table.Column<string>(nullable: true),
                    artist_name = table.Column<string>(nullable: true),
                    playcount = table.Column<int>(nullable: false),
                    last_updated = table.Column<DateTime>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_track", x => x.user_track_id);
                    table.ForeignKey(
                        name: "fk_user_track_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_tracks_album_id",
                table: "tracks",
                column: "album_id");

            migrationBuilder.CreateIndex(
                name: "ix_album_artist_id",
                table: "album",
                column: "artist_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_album_user_id",
                table: "user_album",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_track_user_id",
                table: "user_track",
                column: "user_id");

            migrationBuilder.AddForeignKey(
                name: "fk_tracks_album_album_id",
                table: "tracks",
                column: "album_id",
                principalTable: "album",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_tracks_album_album_id",
                table: "tracks");

            migrationBuilder.DropTable(
                name: "album");

            migrationBuilder.DropTable(
                name: "user_album");

            migrationBuilder.DropTable(
                name: "user_track");

            migrationBuilder.DropPrimaryKey(
                name: "pk_user_artists",
                table: "user_artists");

            migrationBuilder.DropIndex(
                name: "ix_tracks_album_id",
                table: "tracks");

            migrationBuilder.DropColumn(
                name: "last_updated",
                table: "users");

            migrationBuilder.DropColumn(
                name: "user_artist_id",
                table: "user_artists");

            migrationBuilder.DropColumn(
                name: "album_id",
                table: "tracks");

            migrationBuilder.DropColumn(
                name: "popularity",
                table: "tracks");

            migrationBuilder.DropColumn(
                name: "genres",
                table: "artists");

            migrationBuilder.DropColumn(
                name: "popularity",
                table: "artists");

            migrationBuilder.AddColumn<int>(
                name: "artist_id",
                table: "user_artists",
                type: "integer",
                nullable: false,
                defaultValue: 0)
                .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

            migrationBuilder.AddPrimaryKey(
                name: "pk_user_artists",
                table: "user_artists",
                column: "artist_id");
        }
    }
}
