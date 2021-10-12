using Microsoft.EntityFrameworkCore.Migrations;

namespace FMBot.Persistence.EntityFrameWork.Migrations
{
    public partial class AddLfmCoverUrlAndTrackFeatures : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<float>(
                name: "acousticness",
                table: "tracks",
                type: "real",
                nullable: true);

            migrationBuilder.AddColumn<float>(
                name: "danceability",
                table: "tracks",
                type: "real",
                nullable: true);

            migrationBuilder.AddColumn<float>(
                name: "energy",
                table: "tracks",
                type: "real",
                nullable: true);

            migrationBuilder.AddColumn<float>(
                name: "instrumentalness",
                table: "tracks",
                type: "real",
                nullable: true);

            migrationBuilder.AddColumn<float>(
                name: "liveness",
                table: "tracks",
                type: "real",
                nullable: true);

            migrationBuilder.AddColumn<float>(
                name: "loudness",
                table: "tracks",
                type: "real",
                nullable: true);

            migrationBuilder.AddColumn<float>(
                name: "speechiness",
                table: "tracks",
                type: "real",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "lastfm_image_url",
                table: "albums",
                type: "text",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "acousticness",
                table: "tracks");

            migrationBuilder.DropColumn(
                name: "danceability",
                table: "tracks");

            migrationBuilder.DropColumn(
                name: "energy",
                table: "tracks");

            migrationBuilder.DropColumn(
                name: "instrumentalness",
                table: "tracks");

            migrationBuilder.DropColumn(
                name: "liveness",
                table: "tracks");

            migrationBuilder.DropColumn(
                name: "loudness",
                table: "tracks");

            migrationBuilder.DropColumn(
                name: "speechiness",
                table: "tracks");

            migrationBuilder.DropColumn(
                name: "lastfm_image_url",
                table: "albums");

            migrationBuilder.RenameColumn(
                name: "valence",
                table: "tracks",
                newName: "tempo");
        }
    }
}
