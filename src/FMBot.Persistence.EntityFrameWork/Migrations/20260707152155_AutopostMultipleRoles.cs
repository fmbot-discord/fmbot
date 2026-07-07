using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FMBot.Persistence.EntityFrameWork.Migrations
{
    /// <inheritdoc />
    public partial class AutopostMultipleRoles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "role_id",
                table: "guild_autoposts");

            migrationBuilder.AddColumn<decimal[]>(
                name: "role_ids",
                table: "guild_autoposts",
                type: "numeric(20,0)[]",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "role_ids",
                table: "guild_autoposts");

            migrationBuilder.AddColumn<decimal>(
                name: "role_id",
                table: "guild_autoposts",
                type: "numeric(20,0)",
                nullable: true);
        }
    }
}
