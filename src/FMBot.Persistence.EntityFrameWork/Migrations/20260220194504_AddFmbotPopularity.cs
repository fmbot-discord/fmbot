using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FMBot.Persistence.EntityFrameWork.Migrations
{
    /// <inheritdoc />
    public partial class AddFmbotPopularity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "fmbot_popularity",
                table: "artists",
                type: "INTEGER",
                nullable: true);
            migrationBuilder.AddColumn<int>(
                name: "fmbot_popularity",
                table: "albums",
                type: "INTEGER",
                nullable: true);
            migrationBuilder.AddColumn<int>(
                name: "fmbot_popularity",
                table: "tracks",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "fmbot_popularity",
                table: "artists");
            migrationBuilder.DropColumn(
                name: "fmbot_popularity",
                table: "albums");
            migrationBuilder.DropColumn(
                name: "fmbot_popularity",
                table: "tracks");
        }
    }
}
