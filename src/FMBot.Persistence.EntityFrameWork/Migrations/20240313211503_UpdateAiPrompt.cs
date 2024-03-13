using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FMBot.Persistence.EntityFrameWork.Migrations
{
    /// <inheritdoc />
    public partial class UpdateAiPrompt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "free_model",
                table: "ai_prompts",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "premium_model",
                table: "ai_prompts",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "free_model",
                table: "ai_prompts");

            migrationBuilder.DropColumn(
                name: "premium_model",
                table: "ai_prompts");
        }
    }
}
