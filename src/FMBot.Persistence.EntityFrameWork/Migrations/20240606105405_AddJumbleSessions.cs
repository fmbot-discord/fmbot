using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace FMBot.Persistence.EntityFrameWork.Migrations
{
    /// <inheritdoc />
    public partial class AddJumbleSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "jumble_sessions",
                columns: table => new
                {
                    jumble_session_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    starter_user_id = table.Column<int>(type: "integer", nullable: false),
                    discord_guild_id = table.Column<decimal>(type: "numeric(20,0)", nullable: true),
                    discord_channel_id = table.Column<decimal>(type: "numeric(20,0)", nullable: true),
                    discord_id = table.Column<decimal>(type: "numeric(20,0)", nullable: true),
                    discord_response_id = table.Column<decimal>(type: "numeric(20,0)", nullable: true),
                    jumble_type = table.Column<int>(type: "integer", nullable: false),
                    date_started = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    date_ended = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    reshuffles = table.Column<int>(type: "integer", nullable: false),
                    jumbled_artist = table.Column<string>(type: "text", nullable: true),
                    correct_answer = table.Column<string>(type: "text", nullable: true),
                    hints = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_jumble_sessions", x => x.jumble_session_id);
                });

            migrationBuilder.CreateTable(
                name: "jumble_session_answers",
                columns: table => new
                {
                    jumble_session_answer_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    jumble_session_id = table.Column<int>(type: "integer", nullable: false),
                    date_answered = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    discord_user_id = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    correct = table.Column<bool>(type: "boolean", nullable: false),
                    answer = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_jumble_session_answers", x => x.jumble_session_answer_id);
                    table.ForeignKey(
                        name: "fk_jumble_session_answers_jumble_sessions_jumble_session_id",
                        column: x => x.jumble_session_id,
                        principalTable: "jumble_sessions",
                        principalColumn: "jumble_session_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_jumble_session_answers_jumble_session_id",
                table: "jumble_session_answers",
                column: "jumble_session_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "jumble_session_answers");

            migrationBuilder.DropTable(
                name: "jumble_sessions");
        }
    }
}
