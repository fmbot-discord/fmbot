using Microsoft.EntityFrameworkCore.Migrations;

namespace FMBot.Persistence.EntityFrameWork.Migrations
{
    public partial class UpdateInactiveUserTableCascade : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_inactive_users_users_user_id",
                table: "inactive_users");

            migrationBuilder.AddForeignKey(
                name: "fk_inactive_users_users_user_id",
                table: "inactive_users",
                column: "user_id",
                principalTable: "users",
                principalColumn: "user_id",
                onDelete: ReferentialAction.Cascade);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_inactive_users_users_user_id",
                table: "inactive_users");

            migrationBuilder.AddForeignKey(
                name: "fk_inactive_users_users_user_id",
                table: "inactive_users",
                column: "user_id",
                principalTable: "users",
                principalColumn: "user_id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
