using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FMBot.Persistence.EntityFrameWork.Migrations
{
    /// <inheritdoc />
    public partial class AddMusicBrainzTrackData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "disambiguation",
                table: "tracks",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "language",
                table: "tracks",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "music_brainz_date",
                table: "tracks",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "disambiguation",
                table: "tracks");

            migrationBuilder.DropColumn(
                name: "language",
                table: "tracks");

            migrationBuilder.DropColumn(
                name: "music_brainz_date",
                table: "tracks");
        }
    }
}
