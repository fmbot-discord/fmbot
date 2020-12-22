using Microsoft.EntityFrameworkCore.Migrations;

namespace FMBot.Persistence.EntityFrameWork.Migrations
{
    public partial class UpdateInactiveUserTable : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "missing_parameters_error_count",
                table: "inactive_users",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "removed",
                table: "inactive_users",
                type: "boolean",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "missing_parameters_error_count",
                table: "inactive_users");

            migrationBuilder.DropColumn(
                name: "removed",
                table: "inactive_users");
        }
    }
}
