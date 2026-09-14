using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FMBot.Persistence.EntityFrameWork.Migrations
{
    /// <inheritdoc />
    public partial class AddDeezerData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_album_genres_album_id_name",
                table: "album_genres");

            migrationBuilder.AddColumn<DateTime>(
                name: "deezer_date",
                table: "tracks",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "deezer_id",
                table: "tracks",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "explicit",
                table: "tracks",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "deezer_date",
                table: "artists",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "deezer_id",
                table: "artists",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "deezer_date",
                table: "albums",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "deezer_id",
                table: "albums",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "explicit",
                table: "albums",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "source",
                table: "album_genres",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "source_genre_id",
                table: "album_genres",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_album_genres_album_id_source_name",
                table: "album_genres",
                columns: new[] { "album_id", "source", "name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_album_genres_album_id_source_name",
                table: "album_genres");

            migrationBuilder.DropColumn(
                name: "deezer_date",
                table: "tracks");

            migrationBuilder.DropColumn(
                name: "deezer_id",
                table: "tracks");

            migrationBuilder.DropColumn(
                name: "explicit",
                table: "tracks");

            migrationBuilder.DropColumn(
                name: "deezer_date",
                table: "artists");

            migrationBuilder.DropColumn(
                name: "deezer_id",
                table: "artists");

            migrationBuilder.DropColumn(
                name: "deezer_date",
                table: "albums");

            migrationBuilder.DropColumn(
                name: "deezer_id",
                table: "albums");

            migrationBuilder.DropColumn(
                name: "explicit",
                table: "albums");

            migrationBuilder.DropColumn(
                name: "source",
                table: "album_genres");

            migrationBuilder.DropColumn(
                name: "source_genre_id",
                table: "album_genres");

            migrationBuilder.CreateIndex(
                name: "ix_album_genres_album_id_name",
                table: "album_genres",
                columns: new[] { "album_id", "name" },
                unique: true);
        }
    }
}
