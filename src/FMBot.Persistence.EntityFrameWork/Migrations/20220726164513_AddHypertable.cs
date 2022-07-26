using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FMBot.Persistence.EntityFrameWork.Migrations
{
    public partial class AddHypertable : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "user_play_ts",
                columns: table => new
                {
                    time_played = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    track_name = table.Column<string>(type: "citext", nullable: true),
                    album_name = table.Column<string>(type: "citext", nullable: true),
                    artist_name = table.Column<string>(type: "citext", nullable: true)
                },
                constraints: table =>
                {
                });

            // Custom: Create timescaledb hypertable
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS timescaledb CASCADE;");
            migrationBuilder.Sql("SELECT create_hypertable('user_play_ts', 'time_played');");
            migrationBuilder.Sql("CREATE INDEX ON user_play_ts (user_id, time_played DESC);");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "user_play_ts");
        }
    }
}
