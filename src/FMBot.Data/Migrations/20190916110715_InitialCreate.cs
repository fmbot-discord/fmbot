using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

namespace FMBot.Data.Migrations
{
    public partial class InitialCreate : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Guilds",
                columns: table => new
                {
                    GuildID = table.Column<int>(nullable: false)
                        .Annotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn),
                    DiscordGuildID = table.Column<string>(nullable: true),
                    Name = table.Column<string>(nullable: true),
                    Blacklisted = table.Column<bool>(nullable: true),
                    TitlesEnabled = table.Column<bool>(nullable: true),
                    ChartType = table.Column<int>(nullable: false),
                    ChartTimePeriod = table.Column<int>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_dbo.Guilds", x => x.GuildID);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    UserID = table.Column<int>(nullable: false)
                        .Annotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn),
                    DiscordUserID = table.Column<string>(nullable: true),
                    Featured = table.Column<bool>(nullable: true),
                    Blacklisted = table.Column<bool>(nullable: true),
                    UserType = table.Column<int>(nullable: false),
                    TitlesEnabled = table.Column<bool>(nullable: true),
                    UserNameLastFM = table.Column<string>(nullable: true),
                    ChartType = table.Column<int>(nullable: false),
                    ChartTimePeriod = table.Column<int>(nullable: false),
                    LastGeneratedChartDateTimeUtc = table.Column<DateTime>(type: "datetime", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_dbo.Users", x => x.UserID);
                });

            migrationBuilder.CreateTable(
                name: "Friends",
                columns: table => new
                {
                    FriendID = table.Column<int>(nullable: false)
                        .Annotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn),
                    UserID = table.Column<int>(nullable: false),
                    LastFMUserName = table.Column<string>(nullable: true),
                    FriendUserID = table.Column<int>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_dbo.Friends", x => x.FriendID);
                    table.ForeignKey(
                        name: "FK_dbo.Friends_dbo.Users_FriendUserID",
                        column: x => x.FriendUserID,
                        principalTable: "Users",
                        principalColumn: "UserID",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_dbo.Friends_dbo.Users_UserID",
                        column: x => x.UserID,
                        principalTable: "Users",
                        principalColumn: "UserID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GuildUsers",
                columns: table => new
                {
                    GuildID = table.Column<int>(nullable: false),
                    UserID = table.Column<int>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_dbo.GuildUsers", x => new { x.GuildID, x.UserID });
                    table.ForeignKey(
                        name: "FK_dbo.GuildUsers_dbo.Guilds_GuildID",
                        column: x => x.GuildID,
                        principalTable: "Guilds",
                        principalColumn: "GuildID",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_dbo.GuildUsers_dbo.Users_UserID",
                        column: x => x.UserID,
                        principalTable: "Users",
                        principalColumn: "UserID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FriendUserID",
                table: "Friends",
                column: "FriendUserID");

            migrationBuilder.CreateIndex(
                name: "IX_UserID",
                table: "Friends",
                column: "UserID");

            migrationBuilder.CreateIndex(
                name: "IX_GuildID",
                table: "GuildUsers",
                column: "GuildID");

            migrationBuilder.CreateIndex(
                name: "IX_UserID",
                table: "GuildUsers",
                column: "UserID");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Friends");

            migrationBuilder.DropTable(
                name: "GuildUsers");

            migrationBuilder.DropTable(
                name: "Guilds");

            migrationBuilder.DropTable(
                name: "Users");
        }
    }
}
