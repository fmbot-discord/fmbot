using Microsoft.EntityFrameworkCore.Migrations;

namespace FMBot.Persistence.EntityFrameWork.Migrations
{
    public partial class MakeAlbumArtistNullable : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_albums_artists_artist_id",
                table: "albums");

            migrationBuilder.AlterColumn<int>(
                name: "artist_id",
                table: "albums",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddForeignKey(
                name: "fk_albums_artists_artist_id",
                table: "albums",
                column: "artist_id",
                principalTable: "artists",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_albums_artists_artist_id",
                table: "albums");

            migrationBuilder.AlterColumn<int>(
                name: "artist_id",
                table: "albums",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "fk_albums_artists_artist_id",
                table: "albums",
                column: "artist_id",
                principalTable: "artists",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
