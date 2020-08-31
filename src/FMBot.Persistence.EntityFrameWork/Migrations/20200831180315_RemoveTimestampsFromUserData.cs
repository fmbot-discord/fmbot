using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace FMBot.Persistence.EntityFrameWork.Migrations
{
    public partial class RemoveTimestampsFromUserData : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "last_updated",
                table: "user_tracks");

            migrationBuilder.DropColumn(
                name: "last_updated",
                table: "user_artists");

            migrationBuilder.DropColumn(
                name: "last_updated",
                table: "user_albums");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "last_updated",
                table: "user_tracks",
                type: "timestamp without time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<DateTime>(
                name: "last_updated",
                table: "user_artists",
                type: "timestamp without time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<DateTime>(
                name: "last_updated",
                table: "user_albums",
                type: "timestamp without time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));
        }
    }
}
