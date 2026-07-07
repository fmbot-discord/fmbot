using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FMBot.Persistence.EntityFrameWork.Migrations
{
    /// <inheritdoc />
    public partial class ServerFeaturedFrequency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "featured_frequency",
                table: "guilds",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "featured_frequency",
                table: "guilds");
        }
    }
}
