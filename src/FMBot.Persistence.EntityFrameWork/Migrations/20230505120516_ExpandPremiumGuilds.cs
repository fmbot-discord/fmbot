using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FMBot.Persistence.EntityFrameWork.Migrations
{
    /// <inheritdoc />
    public partial class ExpandPremiumGuilds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "automatic_crown_seeder",
                table: "guilds",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "last_crown_seed",
                table: "guilds",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "user_activity_threshold_days",
                table: "guilds",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal[]>(
                name: "roles",
                table: "guild_users",
                type: "numeric(20,0)[]",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "automatic_crown_seeder",
                table: "guilds");

            migrationBuilder.DropColumn(
                name: "last_crown_seed",
                table: "guilds");

            migrationBuilder.DropColumn(
                name: "user_activity_threshold_days",
                table: "guilds");

            migrationBuilder.DropColumn(
                name: "roles",
                table: "guild_users");
        }
    }
}
