using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace FMBot.Persistence.EntityFrameWork.Migrations
{
    /// <inheritdoc />
    public partial class AddAutoPoster : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "guild_autoposts",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    guild_id = table.Column<int>(type: "integer", nullable: false),
                    channel_id = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    content_type = table.Column<int>(type: "integer", nullable: false),
                    schedule = table.Column<int>(type: "integer", nullable: false),
                    role_id = table.Column<decimal>(type: "numeric(20,0)", nullable: true),
                    artist_filter = table.Column<string>(type: "text", nullable: true),
                    content_size = table.Column<int>(type: "integer", nullable: false),
                    time_period = table.Column<int>(type: "integer", nullable: true),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    last_posted = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_message_id = table.Column<decimal>(type: "numeric(20,0)", nullable: true),
                    created_by = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    modified = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_guild_autoposts", x => x.id);
                    table.ForeignKey(
                        name: "fk_guild_autoposts_guilds_guild_id",
                        column: x => x.guild_id,
                        principalTable: "guilds",
                        principalColumn: "guild_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "guild_autopost_runs",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    autopost_id = table.Column<int>(type: "integer", nullable: false),
                    guild_id = table.Column<int>(type: "integer", nullable: false),
                    content_type = table.Column<int>(type: "integer", nullable: false),
                    period_start = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    period_end = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    posted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    message_id = table.Column<decimal>(type: "numeric(20,0)", nullable: true),
                    snapshot = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_guild_autopost_runs", x => x.id);
                    table.ForeignKey(
                        name: "fk_guild_autopost_runs_guild_autoposts_autopost_id",
                        column: x => x.autopost_id,
                        principalTable: "guild_autoposts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_guild_autopost_runs_autopost_id",
                table: "guild_autopost_runs",
                column: "autopost_id");

            migrationBuilder.CreateIndex(
                name: "ix_guild_autoposts_channel_id",
                table: "guild_autoposts",
                column: "channel_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_guild_autoposts_guild_id",
                table: "guild_autoposts",
                column: "guild_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "guild_autopost_runs");

            migrationBuilder.DropTable(
                name: "guild_autoposts");
        }
    }
}
