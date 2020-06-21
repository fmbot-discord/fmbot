using Microsoft.EntityFrameworkCore.Migrations;

namespace FMBot.Persistence.EntityFrameWork.Migrations
{
    public partial class FixGuildUsers : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_guild_user_guilds_guild_id",
                table: "guild_user");

            migrationBuilder.DropForeignKey(
                name: "fk_guild_user_users_user_id",
                table: "guild_user");

            migrationBuilder.DropPrimaryKey(
                name: "pk_guild_user",
                table: "guild_user");

            migrationBuilder.RenameTable(
                name: "guild_user",
                newName: "guild_users");

            migrationBuilder.RenameIndex(
                name: "ix_guild_user_user_id",
                table: "guild_users",
                newName: "ix_guild_users_user_id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_guild_users",
                table: "guild_users",
                columns: new[] { "guild_id", "user_id" });

            migrationBuilder.AddForeignKey(
                name: "fk_guild_users_guilds_guild_id",
                table: "guild_users",
                column: "guild_id",
                principalTable: "guilds",
                principalColumn: "guild_id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_guild_users_users_user_id",
                table: "guild_users",
                column: "user_id",
                principalTable: "users",
                principalColumn: "user_id",
                onDelete: ReferentialAction.Cascade);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_guild_users_guilds_guild_id",
                table: "guild_users");

            migrationBuilder.DropForeignKey(
                name: "fk_guild_users_users_user_id",
                table: "guild_users");

            migrationBuilder.DropPrimaryKey(
                name: "pk_guild_users",
                table: "guild_users");

            migrationBuilder.RenameTable(
                name: "guild_users",
                newName: "guild_user");

            migrationBuilder.RenameIndex(
                name: "ix_guild_users_user_id",
                table: "guild_user",
                newName: "ix_guild_user_user_id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_guild_user",
                table: "guild_user",
                columns: new[] { "guild_id", "user_id" });

            migrationBuilder.AddForeignKey(
                name: "fk_guild_user_guilds_guild_id",
                table: "guild_user",
                column: "guild_id",
                principalTable: "guilds",
                principalColumn: "guild_id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_guild_user_users_user_id",
                table: "guild_user",
                column: "user_id",
                principalTable: "users",
                principalColumn: "user_id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
