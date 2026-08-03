using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FMBot.Persistence.EntityFrameWork.Migrations
{
    /// <inheritdoc />
    public partial class AdjustRecommendedAltChannel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "recommended_alternative_channel_id",
                table: "channels");

            migrationBuilder.AddColumn<decimal[]>(
                name: "recommended_alternative_channel_ids",
                table: "channels",
                type: "numeric(20,0)[]",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "recommended_alternative_channel_ids",
                table: "channels");

            migrationBuilder.AddColumn<decimal>(
                name: "recommended_alternative_channel_id",
                table: "channels",
                type: "numeric(20,0)",
                nullable: true);
        }
    }
}
