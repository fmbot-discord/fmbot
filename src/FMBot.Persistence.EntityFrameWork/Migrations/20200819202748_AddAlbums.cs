using Microsoft.EntityFrameworkCore.Migrations;

namespace FMBot.Persistence.EntityFrameWork.Migrations
{
    public partial class AddAlbums : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_album_artists_artist_id",
                table: "album");

            migrationBuilder.DropForeignKey(
                name: "fk_tracks_album_album_id",
                table: "tracks");

            migrationBuilder.DropForeignKey(
                name: "fk_user_album_users_user_id",
                table: "user_album");

            migrationBuilder.DropForeignKey(
                name: "fk_user_track_users_user_id",
                table: "user_track");

            migrationBuilder.DropPrimaryKey(
                name: "pk_user_track",
                table: "user_track");

            migrationBuilder.DropPrimaryKey(
                name: "pk_user_album",
                table: "user_album");

            migrationBuilder.DropPrimaryKey(
                name: "pk_album",
                table: "album");

            migrationBuilder.RenameTable(
                name: "user_track",
                newName: "user_tracks");

            migrationBuilder.RenameTable(
                name: "user_album",
                newName: "user_albums");

            migrationBuilder.RenameTable(
                name: "album",
                newName: "albums");

            migrationBuilder.RenameIndex(
                name: "ix_user_track_user_id",
                table: "user_tracks",
                newName: "ix_user_tracks_user_id");

            migrationBuilder.RenameIndex(
                name: "ix_user_album_user_id",
                table: "user_albums",
                newName: "ix_user_albums_user_id");

            migrationBuilder.RenameIndex(
                name: "ix_album_artist_id",
                table: "albums",
                newName: "ix_albums_artist_id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_user_tracks",
                table: "user_tracks",
                column: "user_track_id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_user_albums",
                table: "user_albums",
                column: "user_album_id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_albums",
                table: "albums",
                column: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_albums_artists_artist_id",
                table: "albums",
                column: "artist_id",
                principalTable: "artists",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_tracks_albums_album_id",
                table: "tracks",
                column: "album_id",
                principalTable: "albums",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_user_albums_users_user_id",
                table: "user_albums",
                column: "user_id",
                principalTable: "users",
                principalColumn: "user_id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_user_tracks_users_user_id",
                table: "user_tracks",
                column: "user_id",
                principalTable: "users",
                principalColumn: "user_id",
                onDelete: ReferentialAction.Cascade);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_albums_artists_artist_id",
                table: "albums");

            migrationBuilder.DropForeignKey(
                name: "fk_tracks_albums_album_id",
                table: "tracks");

            migrationBuilder.DropForeignKey(
                name: "fk_user_albums_users_user_id",
                table: "user_albums");

            migrationBuilder.DropForeignKey(
                name: "fk_user_tracks_users_user_id",
                table: "user_tracks");

            migrationBuilder.DropPrimaryKey(
                name: "pk_user_tracks",
                table: "user_tracks");

            migrationBuilder.DropPrimaryKey(
                name: "pk_user_albums",
                table: "user_albums");

            migrationBuilder.DropPrimaryKey(
                name: "pk_albums",
                table: "albums");

            migrationBuilder.RenameTable(
                name: "user_tracks",
                newName: "user_track");

            migrationBuilder.RenameTable(
                name: "user_albums",
                newName: "user_album");

            migrationBuilder.RenameTable(
                name: "albums",
                newName: "album");

            migrationBuilder.RenameIndex(
                name: "ix_user_tracks_user_id",
                table: "user_track",
                newName: "ix_user_track_user_id");

            migrationBuilder.RenameIndex(
                name: "ix_user_albums_user_id",
                table: "user_album",
                newName: "ix_user_album_user_id");

            migrationBuilder.RenameIndex(
                name: "ix_albums_artist_id",
                table: "album",
                newName: "ix_album_artist_id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_user_track",
                table: "user_track",
                column: "user_track_id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_user_album",
                table: "user_album",
                column: "user_album_id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_album",
                table: "album",
                column: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_album_artists_artist_id",
                table: "album",
                column: "artist_id",
                principalTable: "artists",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_tracks_album_album_id",
                table: "tracks",
                column: "album_id",
                principalTable: "album",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_user_album_users_user_id",
                table: "user_album",
                column: "user_id",
                principalTable: "users",
                principalColumn: "user_id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_user_track_users_user_id",
                table: "user_track",
                column: "user_id",
                principalTable: "users",
                principalColumn: "user_id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
