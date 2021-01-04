using Microsoft.EntityFrameworkCore.Migrations;

namespace FMBot.Persistence.EntityFrameWork.Migrations
{
    public partial class UpdateCrowns : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_user_crown_guilds_guild_id",
                table: "user_crown");

            migrationBuilder.DropForeignKey(
                name: "fk_user_crown_users_user_id",
                table: "user_crown");

            migrationBuilder.DropPrimaryKey(
                name: "pk_user_crown",
                table: "user_crown");

            migrationBuilder.RenameTable(
                name: "user_crown",
                newName: "user_crowns");

            migrationBuilder.RenameIndex(
                name: "ix_user_crown_user_id",
                table: "user_crowns",
                newName: "ix_user_crowns_user_id");

            migrationBuilder.RenameIndex(
                name: "ix_user_crown_guild_id",
                table: "user_crowns",
                newName: "ix_user_crowns_guild_id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_user_crowns",
                table: "user_crowns",
                column: "crown_id");

            migrationBuilder.AddForeignKey(
                name: "fk_user_crowns_guilds_guild_id",
                table: "user_crowns",
                column: "guild_id",
                principalTable: "guilds",
                principalColumn: "guild_id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_user_crowns_users_user_id",
                table: "user_crowns",
                column: "user_id",
                principalTable: "users",
                principalColumn: "user_id",
                onDelete: ReferentialAction.Cascade);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_user_crowns_guilds_guild_id",
                table: "user_crowns");

            migrationBuilder.DropForeignKey(
                name: "fk_user_crowns_users_user_id",
                table: "user_crowns");

            migrationBuilder.DropPrimaryKey(
                name: "pk_user_crowns",
                table: "user_crowns");

            migrationBuilder.RenameTable(
                name: "user_crowns",
                newName: "user_crown");

            migrationBuilder.RenameIndex(
                name: "ix_user_crowns_user_id",
                table: "user_crown",
                newName: "ix_user_crown_user_id");

            migrationBuilder.RenameIndex(
                name: "ix_user_crowns_guild_id",
                table: "user_crown",
                newName: "ix_user_crown_guild_id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_user_crown",
                table: "user_crown",
                column: "crown_id");

            migrationBuilder.AddForeignKey(
                name: "fk_user_crown_guilds_guild_id",
                table: "user_crown",
                column: "guild_id",
                principalTable: "guilds",
                principalColumn: "guild_id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_user_crown_users_user_id",
                table: "user_crown",
                column: "user_id",
                principalTable: "users",
                principalColumn: "user_id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
