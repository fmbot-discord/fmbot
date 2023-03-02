using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FMBot.Persistence.EntityFrameWork.Migrations
{
    /// <inheritdoc />
    public partial class UpdateCensoredMusic : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "custom_logo",
                table: "guilds",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "censor_type",
                table: "censored_music",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "custom_logo",
                table: "guilds");

            migrationBuilder.DropColumn(
                name: "censor_type",
                table: "censored_music");
        }
    }
}
