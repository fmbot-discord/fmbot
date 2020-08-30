using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

namespace FMBot.Persistence.EntityFrameWork.Migrations
{
    public partial class RenameArtistIdForBackwardsCompatibility : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "pk_user_artists",
                table: "user_artists");

            migrationBuilder.DropColumn(
                name: "user_artist_id",
                table: "user_artists");

            migrationBuilder.AddColumn<int>(
                name: "artist_id",
                table: "user_artists",
                nullable: false,
                defaultValue: 0)
                .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

            migrationBuilder.AddPrimaryKey(
                name: "pk_user_artists",
                table: "user_artists",
                column: "artist_id");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "pk_user_artists",
                table: "user_artists");

            migrationBuilder.DropColumn(
                name: "artist_id",
                table: "user_artists");

            migrationBuilder.AddColumn<int>(
                name: "user_artist_id",
                table: "user_artists",
                type: "integer",
                nullable: false,
                defaultValue: 0)
                .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

            migrationBuilder.AddPrimaryKey(
                name: "pk_user_artists",
                table: "user_artists",
                column: "user_artist_id");
        }
    }
}
