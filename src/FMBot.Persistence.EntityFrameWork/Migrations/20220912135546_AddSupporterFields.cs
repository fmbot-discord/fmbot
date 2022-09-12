using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FMBot.Persistence.EntityFrameWork.Migrations
{
    public partial class AddSupporterFields : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "discord_user_id",
                table: "supporters",
                type: "numeric(20,0)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "expired",
                table: "supporters",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "last_payment",
                table: "supporters",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "open_collective_id",
                table: "supporters",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "subscription_type",
                table: "supporters",
                type: "integer",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "discord_user_id",
                table: "supporters");

            migrationBuilder.DropColumn(
                name: "expired",
                table: "supporters");

            migrationBuilder.DropColumn(
                name: "last_payment",
                table: "supporters");

            migrationBuilder.DropColumn(
                name: "open_collective_id",
                table: "supporters");

            migrationBuilder.DropColumn(
                name: "subscription_type",
                table: "supporters");
        }
    }
}
