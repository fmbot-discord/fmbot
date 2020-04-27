using Microsoft.EntityFrameworkCore.Migrations;

namespace FMBot.Persistence.EntityFrameWork.Migrations
{
    public partial class AddFmCountTypeToUser : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "chart_type",
                table: "users",
                newName: "fm_embed_type");

            migrationBuilder.RenameColumn(
                name: "chart_type",
                table: "guilds",
                newName: "fm_embed_type");

            migrationBuilder.AddColumn<int>(
                name: "fm_count_type",
                table: "users",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "fm_count_type",
                table: "users");

            migrationBuilder.RenameColumn(
                name: "fm_embed_type",
                table: "users",
                newName: "chart_type");

            migrationBuilder.RenameColumn(
                name: "fm_embed_type",
                table: "guilds",
                newName: "chart_type");
        }
    }
}
