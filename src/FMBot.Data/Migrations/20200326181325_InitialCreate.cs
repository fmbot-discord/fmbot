using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

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
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DiscordGuildID = table.Column<decimal>(nullable: false),
                    Name = table.Column<string>(nullable: true),
                    Blacklisted = table.Column<bool>(nullable: true),
                    TitlesEnabled = table.Column<bool>(nullable: true),
                    ChartType = table.Column<int>(nullable: false),
                    ChartTimePeriod = table.Column<int>(nullable: false),
                    EmoteReactions = table.Column<string>(nullable: true)
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
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DiscordUserID = table.Column<decimal>(nullable: false),
                    Featured = table.Column<bool>(nullable: true),
                    Blacklisted = table.Column<bool>(nullable: true),
                    UserType = table.Column<int>(nullable: false),
                    TitlesEnabled = table.Column<bool>(nullable: true),
                    UserNameLastFM = table.Column<string>(nullable: true),
                    ChartType = table.Column<int>(nullable: false),
                    ChartTimePeriod = table.Column<int>(nullable: false),
                    LastGeneratedChartDateTimeUtc = table.Column<DateTime>(nullable: true)
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
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
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

            migrationBuilder.CreateIndex(
                name: "IX_FriendUserID",
                table: "Friends",
                column: "FriendUserID");

            migrationBuilder.CreateIndex(
                name: "IX_UserID",
                table: "Friends",
                column: "UserID");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Friends");

            migrationBuilder.DropTable(
                name: "Guilds");

            migrationBuilder.DropTable(
                name: "Users");
        }
    }
}
