using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FMBot.Persistence.EntityFrameWork.Migrations
{
    /// <inheritdoc />
    public partial class AddStripeCurrency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "currency",
                table: "stripe_supporters",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "currency",
                table: "stripe_pricing",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "currency",
                table: "stripe_supporters");

            migrationBuilder.DropColumn(
                name: "currency",
                table: "stripe_pricing");
        }
    }
}
