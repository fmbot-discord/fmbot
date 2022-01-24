using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FMBot.Persistence.EntityFrameWork.Migrations
{
    public partial class AddLastfmCacheDates : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "last_fm_url",
                table: "tracks",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "lastfm_date",
                table: "tracks",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "supporter_day",
                table: "featured_logs",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "lastfm_date",
                table: "artists",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "lastfm_date",
                table: "albums",
                type: "timestamp with time zone",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "last_fm_url",
                table: "tracks");

            migrationBuilder.DropColumn(
                name: "lastfm_date",
                table: "tracks");

            migrationBuilder.DropColumn(
                name: "supporter_day",
                table: "featured_logs");

            migrationBuilder.DropColumn(
                name: "lastfm_date",
                table: "artists");

            migrationBuilder.DropColumn(
                name: "lastfm_date",
                table: "albums");
        }
    }
}
