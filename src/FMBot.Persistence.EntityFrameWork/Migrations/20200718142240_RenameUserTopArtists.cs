using Microsoft.EntityFrameworkCore.Migrations;

namespace FMBot.Persistence.EntityFrameWork.Migrations
{
    public partial class RenameUserTopArtists : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_artists_users_user_id",
                table: "artists");

            migrationBuilder.DropPrimaryKey(
                name: "pk_artists",
                table: "artists");

            migrationBuilder.RenameTable(
                name: "artists",
                newName: "user_artists");

            migrationBuilder.RenameIndex(
                name: "ix_artists_user_id",
                table: "user_artists",
                newName: "ix_user_artists_user_id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_user_artists",
                table: "user_artists",
                column: "artist_id");

            migrationBuilder.AddForeignKey(
                name: "fk_user_artists_users_user_id",
                table: "user_artists",
                column: "user_id",
                principalTable: "users",
                principalColumn: "user_id",
                onDelete: ReferentialAction.Cascade);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_user_artists_users_user_id",
                table: "user_artists");

            migrationBuilder.DropPrimaryKey(
                name: "pk_user_artists",
                table: "user_artists");

            migrationBuilder.RenameTable(
                name: "user_artists",
                newName: "artists");

            migrationBuilder.RenameIndex(
                name: "ix_user_artists_user_id",
                table: "artists",
                newName: "ix_artists_user_id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_artists",
                table: "artists",
                column: "artist_id");

            migrationBuilder.AddForeignKey(
                name: "fk_artists_users_user_id",
                table: "artists",
                column: "user_id",
                principalTable: "users",
                principalColumn: "user_id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
