using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace FMBot.Persistence.EntityFrameWork.Migrations
{
    /// <inheritdoc />
    public partial class AddGlobalFilteredUsers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "global_filtered_users",
                columns: table => new
                {
                    global_filtered_user_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_name_last_fm = table.Column<string>(type: "text", nullable: true),
                    registered_last_fm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    user_id = table.Column<int>(type: "integer", nullable: true),
                    reason = table.Column<int>(type: "integer", nullable: false),
                    reason_amount = table.Column<int>(type: "integer", nullable: true),
                    occurrence_start = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    occurrence_end = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_global_filtered_users", x => x.global_filtered_user_id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "global_filtered_users");
        }
    }
}
