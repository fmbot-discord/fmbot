using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace FMBot.Persistence.EntityFrameWork.Migrations
{
    /// <inheritdoc />
    public partial class AddJumbleSessionHints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "hints",
                table: "jumble_sessions");

            migrationBuilder.CreateTable(
                name: "jumble_session_hint",
                columns: table => new
                {
                    jumble_session_hint_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    jumble_session_id = table.Column<int>(type: "integer", nullable: false),
                    type = table.Column<int>(type: "integer", nullable: false),
                    content = table.Column<string>(type: "text", nullable: true),
                    hint_shown = table.Column<bool>(type: "boolean", nullable: false),
                    order = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_jumble_session_hint", x => x.jumble_session_hint_id);
                    table.ForeignKey(
                        name: "fk_jumble_session_hint_jumble_sessions_jumble_session_id",
                        column: x => x.jumble_session_id,
                        principalTable: "jumble_sessions",
                        principalColumn: "jumble_session_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_jumble_session_hint_jumble_session_id",
                table: "jumble_session_hint",
                column: "jumble_session_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "jumble_session_hint");

            migrationBuilder.AddColumn<string>(
                name: "hints",
                table: "jumble_sessions",
                type: "jsonb",
                nullable: true);
        }
    }
}
