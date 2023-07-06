using System;
using FMBot.Domain.Enums;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FMBot.Persistence.EntityFrameWork.Migrations
{
    public partial class CleanUserObject : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "featured",
                table: "users");

            migrationBuilder.DropColumn(
                name: "featured_notifications_enabled",
                table: "users");

            migrationBuilder.DropColumn(
                name: "last_generated_chart_date_time_utc",
                table: "users");

            migrationBuilder.DropColumn(
                name: "titles_enabled",
                table: "users");

            migrationBuilder.DropColumn(
                name: "chart_time_period",
                table: "users");

            migrationBuilder.AddColumn<int>(
                name: "data_source",
                table: "users",
                type: "integer",
                nullable: false,
                defaultValue: DataSource.LastFm);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "data_source",
                table: "users",
                newName: "chart_time_period");

            migrationBuilder.AddColumn<bool>(
                name: "featured",
                table: "users",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "featured_notifications_enabled",
                table: "users",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "last_generated_chart_date_time_utc",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "titles_enabled",
                table: "users",
                type: "boolean",
                nullable: true);
        }
    }
}
