using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FMBot.Persistence.EntityFrameWork.Migrations
{
    /// <inheritdoc />
    public partial class FixUserDiscogRelation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_user_discogs_releases_user_discogs_user_discogs_temp_id1",
                table: "user_discogs_releases");

            migrationBuilder.DropIndex(
                name: "ix_user_discogs_releases_user_discogs_user_id",
                table: "user_discogs_releases");

            migrationBuilder.DropColumn(
                name: "user_discogs_user_id",
                table: "user_discogs_releases");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "user_discogs_user_id",
                table: "user_discogs_releases",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_user_discogs_releases_user_discogs_user_id",
                table: "user_discogs_releases",
                column: "user_discogs_user_id");

            migrationBuilder.AddForeignKey(
                name: "fk_user_discogs_releases_user_discogs_user_discogs_temp_id1",
                table: "user_discogs_releases",
                column: "user_discogs_user_id",
                principalTable: "user_discogs",
                principalColumn: "user_id");
        }
    }
}
