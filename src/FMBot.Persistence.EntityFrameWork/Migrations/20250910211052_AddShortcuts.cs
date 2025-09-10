using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace FMBot.Persistence.EntityFrameWork.Migrations
{
    /// <inheritdoc />
    public partial class AddShortcuts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "inactive_users");

            migrationBuilder.CreateTable(
                name: "guild_shortcuts",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    guild_id = table.Column<int>(type: "integer", nullable: false),
                    input = table.Column<string>(type: "text", nullable: true),
                    output = table.Column<string>(type: "text", nullable: true),
                    created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    modified = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_guild_shortcuts", x => x.id);
                    table.ForeignKey(
                        name: "fk_guild_shortcuts_guilds_guild_id",
                        column: x => x.guild_id,
                        principalTable: "guilds",
                        principalColumn: "guild_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_shortcuts",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    input = table.Column<string>(type: "text", nullable: true),
                    output = table.Column<string>(type: "text", nullable: true),
                    created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    modified = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_shortcuts", x => x.id);
                    table.ForeignKey(
                        name: "fk_user_shortcuts_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_guild_shortcuts_guild_id",
                table: "guild_shortcuts",
                column: "guild_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_shortcuts_user_id",
                table: "user_shortcuts",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "guild_shortcuts");

            migrationBuilder.DropTable(
                name: "user_shortcuts");

            migrationBuilder.CreateTable(
                name: "inactive_users",
                columns: table => new
                {
                    inactive_user_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id1 = table.Column<int>(type: "integer", nullable: true),
                    created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    failure_error_count = table.Column<int>(type: "integer", nullable: true),
                    missing_parameters_error_count = table.Column<int>(type: "integer", nullable: true),
                    no_scrobbles_error_count = table.Column<int>(type: "integer", nullable: true),
                    recent_tracks_private_count = table.Column<int>(type: "integer", nullable: true),
                    removed = table.Column<bool>(type: "boolean", nullable: true),
                    updated = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    user_id = table.Column<int>(type: "integer", nullable: true),
                    user_name_last_fm = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inactive_users", x => x.inactive_user_id);
                    table.ForeignKey(
                        name: "fk_inactive_users_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_inactive_users_users_user_id1",
                        column: x => x.user_id1,
                        principalTable: "users",
                        principalColumn: "user_id");
                });

            migrationBuilder.CreateIndex(
                name: "ix_inactive_users_user_id",
                table: "inactive_users",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_inactive_users_user_id1",
                table: "inactive_users",
                column: "user_id1");
        }
    }
}
