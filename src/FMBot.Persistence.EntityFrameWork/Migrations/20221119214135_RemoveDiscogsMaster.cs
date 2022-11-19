using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FMBot.Persistence.EntityFrameWork.Migrations
{
    /// <inheritdoc />
    public partial class RemoveDiscogsMaster : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_discogs_genre_discogs_masters_discogs_master_temp_id",
                table: "discogs_genre");

            migrationBuilder.DropForeignKey(
                name: "fk_discogs_releases_discogs_masters_discogs_master_temp_id1",
                table: "discogs_releases");

            migrationBuilder.DropForeignKey(
                name: "fk_discogs_style_discogs_masters_discogs_master_temp_id2",
                table: "discogs_style");

            migrationBuilder.DropTable(
                name: "discogs_masters");

            migrationBuilder.DropIndex(
                name: "ix_discogs_releases_master_id",
                table: "discogs_releases");

            migrationBuilder.RenameColumn(
                name: "master_id",
                table: "discogs_style",
                newName: "release_id");

            migrationBuilder.RenameIndex(
                name: "ix_discogs_style_master_id",
                table: "discogs_style",
                newName: "ix_discogs_style_release_id");

            migrationBuilder.RenameColumn(
                name: "master_id",
                table: "discogs_genre",
                newName: "release_id");

            migrationBuilder.RenameIndex(
                name: "ix_discogs_genre_master_id",
                table: "discogs_genre",
                newName: "ix_discogs_genre_release_id");

            migrationBuilder.AlterColumn<int>(
                name: "master_id",
                table: "discogs_releases",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddColumn<int>(
                name: "album_id",
                table: "discogs_releases",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "artist",
                table: "discogs_releases",
                type: "citext",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "artist_discogs_id",
                table: "discogs_releases",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "artist_id",
                table: "discogs_releases",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "featuring_artist",
                table: "discogs_releases",
                type: "citext",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "featuring_artist_discogs_id",
                table: "discogs_releases",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "featuring_artist_id",
                table: "discogs_releases",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "featuring_artist_join",
                table: "discogs_releases",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "title",
                table: "discogs_releases",
                type: "citext",
                nullable: true);

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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_discogs_genre_discogs_releases_discogs_release_temp_id1",
                table: "discogs_genre");

            migrationBuilder.DropForeignKey(
                name: "fk_discogs_style_discogs_releases_discogs_release_temp_id2",
                table: "discogs_style");

            migrationBuilder.DropColumn(
                name: "album_id",
                table: "discogs_releases");

            migrationBuilder.DropColumn(
                name: "artist",
                table: "discogs_releases");

            migrationBuilder.DropColumn(
                name: "artist_discogs_id",
                table: "discogs_releases");

            migrationBuilder.DropColumn(
                name: "artist_id",
                table: "discogs_releases");

            migrationBuilder.DropColumn(
                name: "featuring_artist",
                table: "discogs_releases");

            migrationBuilder.DropColumn(
                name: "featuring_artist_discogs_id",
                table: "discogs_releases");

            migrationBuilder.DropColumn(
                name: "featuring_artist_id",
                table: "discogs_releases");

            migrationBuilder.DropColumn(
                name: "featuring_artist_join",
                table: "discogs_releases");

            migrationBuilder.DropColumn(
                name: "title",
                table: "discogs_releases");

            migrationBuilder.RenameColumn(
                name: "release_id",
                table: "discogs_style",
                newName: "master_id");

            migrationBuilder.RenameIndex(
                name: "ix_discogs_style_release_id",
                table: "discogs_style",
                newName: "ix_discogs_style_master_id");

            migrationBuilder.RenameColumn(
                name: "release_id",
                table: "discogs_genre",
                newName: "master_id");

            migrationBuilder.RenameIndex(
                name: "ix_discogs_genre_release_id",
                table: "discogs_genre",
                newName: "ix_discogs_genre_master_id");

            migrationBuilder.AlterColumn<int>(
                name: "master_id",
                table: "discogs_releases",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.CreateTable(
                name: "discogs_masters",
                columns: table => new
                {
                    discogsid = table.Column<int>(name: "discogs_id", type: "integer", nullable: false),
                    artist = table.Column<string>(type: "citext", nullable: true),
                    artistdiscogsid = table.Column<int>(name: "artist_discogs_id", type: "integer", nullable: false),
                    artistid = table.Column<int>(name: "artist_id", type: "integer", nullable: true),
                    country = table.Column<string>(type: "text", nullable: true),
                    featuringartist = table.Column<string>(name: "featuring_artist", type: "citext", nullable: true),
                    featuringartistdiscogsid = table.Column<int>(name: "featuring_artist_discogs_id", type: "integer", nullable: true),
                    featuringartistid = table.Column<int>(name: "featuring_artist_id", type: "integer", nullable: true),
                    featuringartistjoin = table.Column<string>(name: "featuring_artist_join", type: "text", nullable: true),
                    title = table.Column<string>(type: "citext", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_discogs_masters", x => x.discogsid);
                });

            migrationBuilder.CreateIndex(
                name: "ix_discogs_releases_master_id",
                table: "discogs_releases",
                column: "master_id");

            migrationBuilder.AddForeignKey(
                name: "fk_discogs_genre_discogs_masters_discogs_master_temp_id",
                table: "discogs_genre",
                column: "master_id",
                principalTable: "discogs_masters",
                principalColumn: "discogs_id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_discogs_releases_discogs_masters_discogs_master_temp_id1",
                table: "discogs_releases",
                column: "master_id",
                principalTable: "discogs_masters",
                principalColumn: "discogs_id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_discogs_style_discogs_masters_discogs_master_temp_id2",
                table: "discogs_style",
                column: "master_id",
                principalTable: "discogs_masters",
                principalColumn: "discogs_id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
