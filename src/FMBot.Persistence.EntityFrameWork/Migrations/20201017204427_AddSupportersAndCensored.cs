using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

namespace FMBot.Persistence.EntityFrameWork.Migrations
{
    public partial class AddSupportersAndCensored : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "disable_supporter_messages",
                table: "guilds",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "censored_music",
                columns: table => new
                {
                    censored_music_id = table.Column<int>(nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    artist_name = table.Column<string>(nullable: true),
                    album_name = table.Column<string>(nullable: true),
                    safe_for_commands = table.Column<bool>(nullable: false),
                    safe_for_featured = table.Column<bool>(nullable: false),
                    artist = table.Column<bool>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_censored_music", x => x.censored_music_id);
                });

            migrationBuilder.CreateTable(
                name: "supporters",
                columns: table => new
                {
                    supporter_id = table.Column<int>(nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(nullable: true),
                    supporter_type = table.Column<int>(nullable: false),
                    notes = table.Column<string>(nullable: true),
                    supporter_messages_enabled = table.Column<bool>(nullable: false),
                    visible_in_overview = table.Column<bool>(nullable: false),
                    created = table.Column<DateTime>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_supporters", x => x.supporter_id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_users_user_id",
                table: "users",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_guilds_guild_id",
                table: "guilds",
                column: "guild_id");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "censored_music");

            migrationBuilder.DropTable(
                name: "supporters");

            migrationBuilder.DropIndex(
                name: "ix_users_user_id",
                table: "users");

            migrationBuilder.DropIndex(
                name: "ix_guilds_guild_id",
                table: "guilds");

            migrationBuilder.DropColumn(
                name: "disable_supporter_messages",
                table: "guilds");
        }
    }
}
