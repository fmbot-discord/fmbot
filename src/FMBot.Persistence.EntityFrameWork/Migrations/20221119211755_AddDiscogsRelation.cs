using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FMBot.Persistence.EntityFrameWork.Migrations
{
    /// <inheritdoc />
    public partial class AddDiscogsRelation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_discogs_format_descriptions_discogs_release_discogs_release",
                table: "discogs_format_descriptions");

            migrationBuilder.DropForeignKey(
                name: "fk_discogs_genre_discogs_master_discogs_master_temp_id1",
                table: "discogs_genre");

            migrationBuilder.DropForeignKey(
                name: "fk_discogs_release_discogs_master_discogs_master_temp_id",
                table: "discogs_release");

            migrationBuilder.DropForeignKey(
                name: "fk_discogs_style_discogs_master_discogs_master_temp_id2",
                table: "discogs_style");

            migrationBuilder.DropForeignKey(
                name: "fk_user_discogs_releases_discogs_release_release_id",
                table: "user_discogs_releases");

            migrationBuilder.DropPrimaryKey(
                name: "pk_discogs_release",
                table: "discogs_release");

            migrationBuilder.DropPrimaryKey(
                name: "pk_discogs_master",
                table: "discogs_master");

            migrationBuilder.RenameTable(
                name: "discogs_release",
                newName: "discogs_releases");

            migrationBuilder.RenameTable(
                name: "discogs_master",
                newName: "discogs_masters");

            migrationBuilder.RenameIndex(
                name: "ix_discogs_release_master_id",
                table: "discogs_releases",
                newName: "ix_discogs_releases_master_id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_discogs_releases",
                table: "discogs_releases",
                column: "discogs_id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_discogs_masters",
                table: "discogs_masters",
                column: "discogs_id");

            migrationBuilder.AddForeignKey(
                name: "fk_discogs_format_descriptions_discogs_releases_discogs_releas",
                table: "discogs_format_descriptions",
                column: "release_id",
                principalTable: "discogs_releases",
                principalColumn: "discogs_id",
                onDelete: ReferentialAction.Cascade);

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

            migrationBuilder.AddForeignKey(
                name: "fk_user_discogs_releases_discogs_releases_release_id",
                table: "user_discogs_releases",
                column: "release_id",
                principalTable: "discogs_releases",
                principalColumn: "discogs_id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_discogs_format_descriptions_discogs_releases_discogs_releas",
                table: "discogs_format_descriptions");

            migrationBuilder.DropForeignKey(
                name: "fk_discogs_genre_discogs_masters_discogs_master_temp_id",
                table: "discogs_genre");

            migrationBuilder.DropForeignKey(
                name: "fk_discogs_releases_discogs_masters_discogs_master_temp_id1",
                table: "discogs_releases");

            migrationBuilder.DropForeignKey(
                name: "fk_discogs_style_discogs_masters_discogs_master_temp_id2",
                table: "discogs_style");

            migrationBuilder.DropForeignKey(
                name: "fk_user_discogs_releases_discogs_releases_release_id",
                table: "user_discogs_releases");

            migrationBuilder.DropPrimaryKey(
                name: "pk_discogs_releases",
                table: "discogs_releases");

            migrationBuilder.DropPrimaryKey(
                name: "pk_discogs_masters",
                table: "discogs_masters");

            migrationBuilder.RenameTable(
                name: "discogs_releases",
                newName: "discogs_release");

            migrationBuilder.RenameTable(
                name: "discogs_masters",
                newName: "discogs_master");

            migrationBuilder.RenameIndex(
                name: "ix_discogs_releases_master_id",
                table: "discogs_release",
                newName: "ix_discogs_release_master_id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_discogs_release",
                table: "discogs_release",
                column: "discogs_id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_discogs_master",
                table: "discogs_master",
                column: "discogs_id");

            migrationBuilder.AddForeignKey(
                name: "fk_discogs_format_descriptions_discogs_release_discogs_release",
                table: "discogs_format_descriptions",
                column: "release_id",
                principalTable: "discogs_release",
                principalColumn: "discogs_id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_discogs_genre_discogs_master_discogs_master_temp_id1",
                table: "discogs_genre",
                column: "master_id",
                principalTable: "discogs_master",
                principalColumn: "discogs_id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_discogs_release_discogs_master_discogs_master_temp_id",
                table: "discogs_release",
                column: "master_id",
                principalTable: "discogs_master",
                principalColumn: "discogs_id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_discogs_style_discogs_master_discogs_master_temp_id2",
                table: "discogs_style",
                column: "master_id",
                principalTable: "discogs_master",
                principalColumn: "discogs_id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_user_discogs_releases_discogs_release_release_id",
                table: "user_discogs_releases",
                column: "release_id",
                principalTable: "discogs_release",
                principalColumn: "discogs_id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
