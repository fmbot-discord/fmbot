using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FMBot.Persistence.EntityFrameWork.Migrations
{
    /// <inheritdoc />
    public partial class ExpandGuilds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "fm_count_type",
                table: "users");

            migrationBuilder.DropColumn(
                name: "blacklisted",
                table: "guilds");

            migrationBuilder.DropColumn(
                name: "chart_time_period",
                table: "guilds");

            migrationBuilder.DropColumn(
                name: "titles_enabled",
                table: "guilds");

            migrationBuilder.AddColumn<decimal[]>(
                name: "allowed_roles",
                table: "guilds",
                type: "numeric(20,0)[]",
                nullable: true);

            migrationBuilder.AddColumn<decimal[]>(
                name: "blocked_roles",
                table: "guilds",
                type: "numeric(20,0)[]",
                nullable: true);

            migrationBuilder.AddColumn<decimal[]>(
                name: "bot_management_roles",
                table: "guilds",
                type: "numeric(20,0)[]",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "last_message",
                table: "guild_users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "who_knows_blocked",
                table: "guild_users",
                type: "boolean",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "allowed_roles",
                table: "guilds");

            migrationBuilder.DropColumn(
                name: "blocked_roles",
                table: "guilds");

            migrationBuilder.DropColumn(
                name: "bot_management_roles",
                table: "guilds");

            migrationBuilder.DropColumn(
                name: "last_message",
                table: "guild_users");

            migrationBuilder.DropColumn(
                name: "who_knows_blocked",
                table: "guild_users");

            migrationBuilder.AddColumn<int>(
                name: "fm_count_type",
                table: "users",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "blacklisted",
                table: "guilds",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "chart_time_period",
                table: "guilds",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "titles_enabled",
                table: "guilds",
                type: "boolean",
                nullable: true);
        }
    }
}
