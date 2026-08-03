using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FMBot.Persistence.EntityFrameWork.Migrations
{
    /// <inheritdoc />
    public partial class AddRecommendedAltChannel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "recommended_alternative_channel_id",
                table: "channels",
                type: "numeric(20,0)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_user_dm_notifications_discord_user_id",
                table: "user_dm_notifications",
                column: "discord_user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_user_dm_notifications_discord_user_id",
                table: "user_dm_notifications");

            migrationBuilder.DropColumn(
                name: "recommended_alternative_channel_id",
                table: "channels");
        }
    }
}
