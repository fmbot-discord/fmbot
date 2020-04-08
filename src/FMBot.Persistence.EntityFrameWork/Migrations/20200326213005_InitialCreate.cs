using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

namespace FMBot.Persistence.EntityFrameWork.Migrations
{
    public partial class InitialCreate : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "guilds",
                columns: table => new
                {
                    guild_id = table.Column<int>(nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    discord_guild_id = table.Column<decimal>(nullable: false),
                    name = table.Column<string>(nullable: true),
                    blacklisted = table.Column<bool>(nullable: true),
                    titles_enabled = table.Column<bool>(nullable: true),
                    chart_type = table.Column<int>(nullable: false),
                    chart_time_period = table.Column<int>(nullable: false),
                    emote_reactions = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_guilds", x => x.guild_id);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    user_id = table.Column<int>(nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    discord_user_id = table.Column<decimal>(nullable: false),
                    featured = table.Column<bool>(nullable: true),
                    blacklisted = table.Column<bool>(nullable: true),
                    user_type = table.Column<int>(nullable: false),
                    titles_enabled = table.Column<bool>(nullable: true),
                    user_name_last_fm = table.Column<string>(nullable: true),
                    chart_type = table.Column<int>(nullable: false),
                    chart_time_period = table.Column<int>(nullable: false),
                    last_generated_chart_date_time_utc = table.Column<DateTime>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_users", x => x.user_id);
                });

            migrationBuilder.CreateTable(
                name: "friends",
                columns: table => new
                {
                    friend_id = table.Column<int>(nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<int>(nullable: false),
                    last_fm_user_name = table.Column<string>(nullable: true),
                    friend_user_id = table.Column<int>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_friends", x => x.friend_id);
                    table.ForeignKey(
                        name: "FK.Friends.Users_FriendUserID",
                        column: x => x.friend_user_id,
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK.Friends.Users_UserID",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_friends_friend_user_id",
                table: "friends",
                column: "friend_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_friends_user_id",
                table: "friends",
                column: "user_id");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "friends");

            migrationBuilder.DropTable(
                name: "guilds");

            migrationBuilder.DropTable(
                name: "users");
        }
    }
}
