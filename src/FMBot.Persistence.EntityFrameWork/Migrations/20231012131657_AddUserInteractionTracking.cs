using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace FMBot.Persistence.EntityFrameWork.Migrations
{
    /// <inheritdoc />
    public partial class AddUserInteractionTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:citext", ",,")
                .Annotation("Npgsql:PostgresExtension:hstore", ",,")
                .Annotation("Npgsql:PostgresExtension:pg_trgm", ",,")
                .OldAnnotation("Npgsql:PostgresExtension:citext", ",,")
                .OldAnnotation("Npgsql:PostgresExtension:pg_trgm", ",,");

            migrationBuilder.AddColumn<decimal>(
                name: "discord_thread_id",
                table: "webhooks",
                type: "numeric(20,0)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ai_prompts",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    type = table.Column<int>(type: "integer", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    language = table.Column<string>(type: "text", nullable: true),
                    prompt = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ai_prompts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "user_interactions",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    discord_guild_id = table.Column<decimal>(type: "numeric(20,0)", nullable: true),
                    discord_channel_id = table.Column<decimal>(type: "numeric(20,0)", nullable: true),
                    discord_id = table.Column<decimal>(type: "numeric(20,0)", nullable: true),
                    type = table.Column<int>(type: "integer", nullable: false),
                    command_name = table.Column<string>(type: "text", nullable: true),
                    command_content = table.Column<string>(type: "text", nullable: true),
                    command_options = table.Column<Dictionary<string, string>>(type: "hstore", nullable: true),
                    response = table.Column<int>(type: "integer", nullable: false),
                    error_reference_id = table.Column<string>(type: "text", nullable: true),
                    error_content = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_interactions", x => x.id);
                    table.ForeignKey(
                        name: "fk_user_interactions_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_user_interactions_user_id",
                table: "user_interactions",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ai_prompts");

            migrationBuilder.DropTable(
                name: "user_interactions");

            migrationBuilder.DropColumn(
                name: "discord_thread_id",
                table: "webhooks");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:citext", ",,")
                .Annotation("Npgsql:PostgresExtension:pg_trgm", ",,")
                .OldAnnotation("Npgsql:PostgresExtension:citext", ",,")
                .OldAnnotation("Npgsql:PostgresExtension:hstore", ",,")
                .OldAnnotation("Npgsql:PostgresExtension:pg_trgm", ",,");
        }
    }
}
