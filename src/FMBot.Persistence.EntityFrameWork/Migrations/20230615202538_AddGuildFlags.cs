using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FMBot.Persistence.EntityFrameWork.Migrations
{
    /// <inheritdoc />
    public partial class AddGuildFlags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal[]>(
                name: "filter_roles",
                table: "guilds",
                type: "numeric(20,0)[]",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "guild_flags",
                table: "guilds",
                type: "bigint",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "filter_roles",
                table: "guilds");

            migrationBuilder.DropColumn(
                name: "guild_flags",
                table: "guilds");
        }
    }
}
