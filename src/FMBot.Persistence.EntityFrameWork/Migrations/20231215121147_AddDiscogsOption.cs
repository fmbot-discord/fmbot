using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FMBot.Persistence.EntityFrameWork.Migrations
{
    /// <inheritdoc />
    public partial class AddDiscogsOption : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "album",
                table: "user_interactions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "artist",
                table: "user_interactions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "track",
                table: "user_interactions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "hide_value",
                table: "user_discogs",
                type: "boolean",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "album",
                table: "user_interactions");

            migrationBuilder.DropColumn(
                name: "artist",
                table: "user_interactions");

            migrationBuilder.DropColumn(
                name: "track",
                table: "user_interactions");

            migrationBuilder.DropColumn(
                name: "hide_value",
                table: "user_discogs");
        }
    }
}
