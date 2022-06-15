using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace FMBot.Persistence.EntityFrameWork.Migrations
{
    public partial class AddStreakSaving : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "user_streaks",
                columns: table => new
                {
                    user_streak_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    track_name = table.Column<string>(type: "citext", nullable: true),
                    track_playcount = table.Column<int>(type: "integer", nullable: true),
                    album_name = table.Column<string>(type: "citext", nullable: true),
                    album_playcount = table.Column<int>(type: "integer", nullable: true),
                    artist_name = table.Column<string>(type: "citext", nullable: true),
                    artist_playcount = table.Column<int>(type: "integer", nullable: true),
                    streak_started = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    streak_ended = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_streaks", x => x.user_streak_id);
                    table.ForeignKey(
                        name: "fk_user_streaks_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_user_streaks_user_id",
                table: "user_streaks",
                column: "user_id");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "user_streaks");
        }
    }
}
