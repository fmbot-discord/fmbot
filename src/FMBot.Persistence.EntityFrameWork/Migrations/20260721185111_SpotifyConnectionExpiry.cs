using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace FMBot.Persistence.EntityFrameWork.Migrations
{
    /// <inheritdoc />
    public partial class SpotifyConnectionExpiry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "dm_channel_id",
                table: "users",
                type: "numeric(20,0)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "spotify_connection_expiry",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "spotify_expiry_checked",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "user_dm_notifications",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    discord_user_id = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    type = table.Column<int>(type: "integer", nullable: false),
                    sent = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    reference = table.Column<string>(type: "text", nullable: true),
                    successful = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_dm_notifications", x => x.id);
                    table.ForeignKey(
                        name: "fk_user_dm_notifications_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_user_dm_notifications_user_id",
                table: "user_dm_notifications",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "user_dm_notifications");

            migrationBuilder.DropColumn(
                name: "dm_channel_id",
                table: "users");

            migrationBuilder.DropColumn(
                name: "spotify_connection_expiry",
                table: "users");

            migrationBuilder.DropColumn(
                name: "spotify_expiry_checked",
                table: "users");
        }
    }
}
