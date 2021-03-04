using Microsoft.EntityFrameworkCore.Migrations;

namespace FMBot.Persistence.EntityFrameWork.Migrations
{
    public partial class AddModeToFeaturedLogs : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "featured_mode",
                table: "featured_logs",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "featured_mode",
                table: "featured_logs");
        }
    }
}
