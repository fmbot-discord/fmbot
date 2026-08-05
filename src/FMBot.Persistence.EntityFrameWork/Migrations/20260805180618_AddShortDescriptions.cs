using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FMBot.Persistence.EntityFrameWork.Migrations
{
    /// <inheritdoc />
    public partial class AddShortDescriptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ai_description",
                table: "tracks",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ai_description_date",
                table: "tracks",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ai_description_hash",
                table: "tracks",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ai_description",
                table: "artists",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ai_description_date",
                table: "artists",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ai_description_hash",
                table: "artists",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ai_description",
                table: "albums",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ai_description_date",
                table: "albums",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ai_description_hash",
                table: "albums",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ai_description",
                table: "tracks");

            migrationBuilder.DropColumn(
                name: "ai_description_date",
                table: "tracks");

            migrationBuilder.DropColumn(
                name: "ai_description_hash",
                table: "tracks");

            migrationBuilder.DropColumn(
                name: "ai_description",
                table: "artists");

            migrationBuilder.DropColumn(
                name: "ai_description_date",
                table: "artists");

            migrationBuilder.DropColumn(
                name: "ai_description_hash",
                table: "artists");

            migrationBuilder.DropColumn(
                name: "ai_description",
                table: "albums");

            migrationBuilder.DropColumn(
                name: "ai_description_date",
                table: "albums");

            migrationBuilder.DropColumn(
                name: "ai_description_hash",
                table: "albums");
        }
    }
}
