using System.Xml.Linq;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FMBot.Persistence.EntityFrameWork.Migrations
{
    /// <inheritdoc />
    public partial class ExpandUserPlays : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "ms_played",
                table: "user_play_ts",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "play_source",
                table: "user_play_ts",
                type: "integer",
            nullable: true);

            migrationBuilder.Sql($"CREATE INDEX ix_user_user_name_last_fm ON users (UPPER(user_name_last_fm));");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ms_played",
                table: "user_play_ts");

            migrationBuilder.DropColumn(
                name: "play_source",
                table: "user_play_ts");
        }
    }
}
