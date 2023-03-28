using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace FMBot.Persistence.EntityFrameWork.Migrations
{
    /// <inheritdoc />
    public partial class AddReportsAndGenerations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ai_generations",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    prompt = table.Column<string>(type: "text", nullable: true),
                    output = table.Column<string>(type: "text", nullable: true),
                    model = table.Column<string>(type: "text", nullable: true),
                    total_tokens = table.Column<int>(type: "integer", nullable: false),
                    date_generated = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ai_generations", x => x.id);
                    table.ForeignKey(
                        name: "fk_ai_generations_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "botted_user_report",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_name_last_fm = table.Column<string>(type: "text", nullable: true),
                    provided_note = table.Column<string>(type: "text", nullable: true),
                    report_status = table.Column<int>(type: "integer", nullable: false),
                    reported_by_discord_user_id = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    processed_by_discord_user_id = table.Column<decimal>(type: "numeric(20,0)", nullable: true),
                    reported_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    processed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_botted_user_report", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "censored_music_report",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    is_artist = table.Column<bool>(type: "boolean", nullable: false),
                    artist_name = table.Column<string>(type: "text", nullable: true),
                    album_name = table.Column<string>(type: "text", nullable: true),
                    provided_note = table.Column<string>(type: "text", nullable: true),
                    report_status = table.Column<int>(type: "integer", nullable: false),
                    reported_by_discord_user_id = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    processed_by_discord_user_id = table.Column<decimal>(type: "numeric(20,0)", nullable: true),
                    artist_id = table.Column<int>(type: "integer", nullable: true),
                    album_id = table.Column<int>(type: "integer", nullable: true),
                    reported_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    processed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_censored_music_report", x => x.id);
                    table.ForeignKey(
                        name: "fk_censored_music_report_albums_album_id",
                        column: x => x.album_id,
                        principalTable: "albums",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_censored_music_report_artists_artist_id",
                        column: x => x.artist_id,
                        principalTable: "artists",
                        principalColumn: "id");
                });

            migrationBuilder.CreateIndex(
                name: "ix_ai_generations_user_id",
                table: "ai_generations",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_censored_music_report_album_id",
                table: "censored_music_report",
                column: "album_id");

            migrationBuilder.CreateIndex(
                name: "ix_censored_music_report_artist_id",
                table: "censored_music_report",
                column: "artist_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ai_generations");

            migrationBuilder.DropTable(
                name: "botted_user_report");

            migrationBuilder.DropTable(
                name: "censored_music_report");
        }
    }
}
