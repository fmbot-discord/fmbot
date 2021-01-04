using Microsoft.EntityFrameworkCore.Migrations;

namespace FMBot.Persistence.EntityFrameWork.Migrations
{
    public partial class AdjustActivityFilter : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "who_knows_eligible_threshold_days",
                table: "guilds",
                newName: "crowns_activity_threshold_days");

            migrationBuilder.RenameColumn(
                name: "crowns_eligible_threshold_days",
                table: "guilds",
                newName: "activity_threshold_days");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "crowns_activity_threshold_days",
                table: "guilds",
                newName: "who_knows_eligible_threshold_days");

            migrationBuilder.RenameColumn(
                name: "activity_threshold_days",
                table: "guilds",
                newName: "crowns_eligible_threshold_days");
        }
    }
}
