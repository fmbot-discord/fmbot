using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FMBot.Persistence.EntityFrameWork.Migrations
{
    /// <inheritdoc />
    public partial class AddSpotifyRemote : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "service",
                table: "user_tokens",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "ix_user_tokens_discord_user_id_bot_type_service",
                table: "user_tokens",
                columns: new[] { "discord_user_id", "bot_type", "service" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_user_tokens_discord_user_id_bot_type_service",
                table: "user_tokens");

            migrationBuilder.DropColumn(
                name: "service",
                table: "user_tokens");
        }
    }
}
