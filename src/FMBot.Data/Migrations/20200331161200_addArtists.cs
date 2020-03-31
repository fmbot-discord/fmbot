using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

namespace FMBot.Data.Migrations
{
    public partial class addArtists : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "featured_notifications_enabled",
                table: "users",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "last_indexed",
                table: "users",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "last_indexed",
                table: "guilds",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "special_guild",
                table: "guilds",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "artists",
                columns: table => new
                {
                    artist_id = table.Column<int>(nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<int>(nullable: false),
                    name = table.Column<string>(nullable: true),
                    playcount = table.Column<int>(nullable: false),
                    last_updated = table.Column<DateTime>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_artists", x => x.artist_id);
                    table.ForeignKey(
                        name: "fk_artists_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_artists_user_id",
                table: "artists",
                column: "user_id");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "artists");

            migrationBuilder.DropColumn(
                name: "featured_notifications_enabled",
                table: "users");

            migrationBuilder.DropColumn(
                name: "last_indexed",
                table: "users");

            migrationBuilder.DropColumn(
                name: "last_indexed",
                table: "guilds");

            migrationBuilder.DropColumn(
                name: "special_guild",
                table: "guilds");
        }
    }
}
