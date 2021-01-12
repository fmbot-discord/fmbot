using FMBot.Domain.Models;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

namespace FMBot.Persistence.EntityFrameWork.Migrations
{
    public partial class AddFeaturedLogAndPrivacy : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "privacy_level",
                table: "users",
                type: "integer",
                nullable: false,
                defaultValue: PrivacyLevel.Public);

            migrationBuilder.CreateTable(
                name: "botted_users",
                columns: table => new
                {
                    botted_user_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_name_last_fm = table.Column<string>(type: "text", nullable: true),
                    notes = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_botted_users", x => x.botted_user_id);
                });

            migrationBuilder.CreateTable(
                name: "featured_logs",
                columns: table => new
                {
                    featured_log_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<int>(type: "integer", nullable: true),
                    bot_type = table.Column<int>(type: "integer", nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    track_name = table.Column<string>(type: "citext", nullable: true),
                    artist_name = table.Column<string>(type: "citext", nullable: true),
                    album_name = table.Column<string>(type: "citext", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_featured_logs", x => x.featured_log_id);
                    table.ForeignKey(
                        name: "fk_featured_logs_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_featured_logs_user_id",
                table: "featured_logs",
                column: "user_id");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "botted_users");

            migrationBuilder.DropTable(
                name: "featured_logs");

            migrationBuilder.DropColumn(
                name: "privacy_level",
                table: "users");
        }
    }
}
