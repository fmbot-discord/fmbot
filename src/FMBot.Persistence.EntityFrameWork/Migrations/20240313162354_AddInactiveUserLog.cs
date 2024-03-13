using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace FMBot.Persistence.EntityFrameWork.Migrations
{
    /// <inheritdoc />
    public partial class AddInactiveUserLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_discogs_format_descriptions_discogs_releases_discogs_releas",
                table: "discogs_format_descriptions");

            migrationBuilder.DropForeignKey(
                name: "fk_discogs_genre_discogs_releases_discogs_release_temp_id1",
                table: "discogs_genre");

            migrationBuilder.DropForeignKey(
                name: "fk_discogs_style_discogs_releases_discogs_release_temp_id2",
                table: "discogs_style");

            migrationBuilder.CreateTable(
                name: "inactive_user_log",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    user_name_last_fm = table.Column<string>(type: "text", nullable: true),
                    response_status = table.Column<int>(type: "integer", nullable: false),
                    created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inactive_user_log", x => x.id);
                    table.ForeignKey(
                        name: "fk_inactive_user_log_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_inactive_user_log_user_id",
                table: "inactive_user_log",
                column: "user_id");

            migrationBuilder.AddForeignKey(
                name: "fk_discogs_format_descriptions_discogs_releases_release_id",
                table: "discogs_format_descriptions",
                column: "release_id",
                principalTable: "discogs_releases",
                principalColumn: "discogs_id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_discogs_genre_discogs_releases_release_id",
                table: "discogs_genre",
                column: "release_id",
                principalTable: "discogs_releases",
                principalColumn: "discogs_id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_discogs_style_discogs_releases_release_id",
                table: "discogs_style",
                column: "release_id",
                principalTable: "discogs_releases",
                principalColumn: "discogs_id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_discogs_format_descriptions_discogs_releases_release_id",
                table: "discogs_format_descriptions");

            migrationBuilder.DropForeignKey(
                name: "fk_discogs_genre_discogs_releases_release_id",
                table: "discogs_genre");

            migrationBuilder.DropForeignKey(
                name: "fk_discogs_style_discogs_releases_release_id",
                table: "discogs_style");

            migrationBuilder.DropTable(
                name: "inactive_user_log");

            migrationBuilder.AddForeignKey(
                name: "fk_discogs_format_descriptions_discogs_releases_discogs_releas",
                table: "discogs_format_descriptions",
                column: "release_id",
                principalTable: "discogs_releases",
                principalColumn: "discogs_id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_discogs_genre_discogs_releases_discogs_release_temp_id1",
                table: "discogs_genre",
                column: "release_id",
                principalTable: "discogs_releases",
                principalColumn: "discogs_id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_discogs_style_discogs_releases_discogs_release_temp_id2",
                table: "discogs_style",
                column: "release_id",
                principalTable: "discogs_releases",
                principalColumn: "discogs_id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
