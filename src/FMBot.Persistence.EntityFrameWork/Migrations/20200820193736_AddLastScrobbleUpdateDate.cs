using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace FMBot.Persistence.EntityFrameWork.Migrations
{
    public partial class AddLastScrobbleUpdateDate : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "last_scrobble_update",
                table: "users",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "last_scrobble_update",
                table: "users");
        }
    }
}
