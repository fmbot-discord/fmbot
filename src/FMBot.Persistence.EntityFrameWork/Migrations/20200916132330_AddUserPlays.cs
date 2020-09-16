using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

namespace FMBot.Persistence.EntityFrameWork.Migrations
{
    public partial class AddUserPlays : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "pk_user_artists",
                table: "user_artists");

            migrationBuilder.DropColumn(
                name: "blacklisted",
                table: "users");

            migrationBuilder.DropColumn(
                name: "artist_id",
                table: "user_artists");

            migrationBuilder.AddColumn<bool>(
                name: "blocked",
                table: "users",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "user_artist_id",
                table: "user_artists",
                nullable: false,
                defaultValue: 0)
                .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

            migrationBuilder.AddPrimaryKey(
                name: "pk_user_artists",
                table: "user_artists",
                column: "user_artist_id");

            migrationBuilder.CreateTable(
                name: "inactive_users",
                columns: table => new
                {
                    inactive_user_id = table.Column<int>(nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<int>(nullable: true),
                    user_name_last_fm = table.Column<string>(nullable: true),
                    recent_tracks_private_count = table.Column<int>(nullable: true),
                    no_scrobbles_error_count = table.Column<int>(nullable: true),
                    failure_error_count = table.Column<int>(nullable: true),
                    created = table.Column<DateTime>(nullable: false),
                    updated = table.Column<DateTime>(nullable: false),
                    user_id1 = table.Column<int>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inactive_users", x => x.inactive_user_id);
                    table.ForeignKey(
                        name: "fk_inactive_users_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_inactive_users_users_user_id1",
                        column: x => x.user_id1,
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "user_plays",
                columns: table => new
                {
                    user_play_id = table.Column<int>(nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<int>(nullable: false),
                    track_name = table.Column<string>(nullable: true),
                    album_name = table.Column<string>(nullable: true),
                    artist_name = table.Column<string>(nullable: true),
                    time_played = table.Column<DateTime>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_plays", x => x.user_play_id);
                    table.ForeignKey(
                        name: "fk_user_plays_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_inactive_users_user_id",
                table: "inactive_users",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_inactive_users_user_id1",
                table: "inactive_users",
                column: "user_id1");

            migrationBuilder.CreateIndex(
                name: "ix_user_plays_user_id",
                table: "user_plays",
                column: "user_id");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "inactive_users");

            migrationBuilder.DropTable(
                name: "user_plays");

            migrationBuilder.DropPrimaryKey(
                name: "pk_user_artists",
                table: "user_artists");

            migrationBuilder.DropColumn(
                name: "blocked",
                table: "users");

            migrationBuilder.DropColumn(
                name: "user_artist_id",
                table: "user_artists");

            migrationBuilder.AddColumn<bool>(
                name: "blacklisted",
                table: "users",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "artist_id",
                table: "user_artists",
                type: "integer",
                nullable: false,
                defaultValue: 0)
                .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

            migrationBuilder.AddPrimaryKey(
                name: "pk_user_artists",
                table: "user_artists",
                column: "artist_id");
        }
    }
}
