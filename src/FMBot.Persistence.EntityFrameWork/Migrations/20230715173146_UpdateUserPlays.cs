using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FMBot.Persistence.EntityFrameWork.Migrations
{
    /// <inheritdoc />
    public partial class UpdateUserPlays : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "ms_played",
                table: "user_plays",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "play_source",
                table: "user_plays",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ms_played",
                table: "user_plays");

            migrationBuilder.DropColumn(
                name: "play_source",
                table: "user_plays");
        }
    }
}
