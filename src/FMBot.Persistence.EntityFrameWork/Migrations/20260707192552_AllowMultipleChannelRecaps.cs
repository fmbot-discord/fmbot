using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FMBot.Persistence.EntityFrameWork.Migrations
{
    /// <inheritdoc />
    public partial class AllowMultipleChannelRecaps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_guild_autoposts_channel_id",
                table: "guild_autoposts");

            migrationBuilder.CreateIndex(
                name: "ix_guild_autoposts_channel_id",
                table: "guild_autoposts",
                column: "channel_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_guild_autoposts_channel_id",
                table: "guild_autoposts");

            migrationBuilder.CreateIndex(
                name: "ix_guild_autoposts_channel_id",
                table: "guild_autoposts",
                column: "channel_id",
                unique: true);
        }
    }
}
