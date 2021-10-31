using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace FMBot.Persistence.EntityFrameWork.Migrations
{
    public partial class AddMusicBrainzDataFields : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "mbid",
                table: "tracks",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "country",
                table: "artists",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "disambiguation",
                table: "artists",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "end_date",
                table: "artists",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "gender",
                table: "artists",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "music_brainz_date",
                table: "artists",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "start_date",
                table: "artists",
                type: "timestamp without time zone",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "mbid",
                table: "tracks");

            migrationBuilder.DropColumn(
                name: "country",
                table: "artists");

            migrationBuilder.DropColumn(
                name: "disambiguation",
                table: "artists");

            migrationBuilder.DropColumn(
                name: "end_date",
                table: "artists");

            migrationBuilder.DropColumn(
                name: "gender",
                table: "artists");

            migrationBuilder.DropColumn(
                name: "music_brainz_date",
                table: "artists");

            migrationBuilder.DropColumn(
                name: "start_date",
                table: "artists");
        }
    }
}
