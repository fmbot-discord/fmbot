using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace FMBot.Persistence.EntityFrameWork.Migrations
{
    /// <inheritdoc />
    public partial class AddPremiumServer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "featured_mode",
                table: "guilds",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "last_recap",
                table: "guilds",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "recap_channel_id",
                table: "guilds",
                type: "numeric(20,0)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "recap_schedule",
                table: "guilds",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "guild_featured_logs",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    guild_id = table.Column<int>(type: "integer", nullable: false),
                    date_time = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    featured_mode = table.Column<int>(type: "integer", nullable: false),
                    user_id = table.Column<int>(type: "integer", nullable: true),
                    description = table.Column<string>(type: "text", nullable: true),
                    image_url = table.Column<string>(type: "text", nullable: true),
                    artist_name = table.Column<string>(type: "text", nullable: true),
                    album_name = table.Column<string>(type: "text", nullable: true),
                    track_name = table.Column<string>(type: "text", nullable: true),
                    has_featured = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_guild_featured_logs", x => x.id);
                    table.ForeignKey(
                        name: "fk_guild_featured_logs_guilds_guild_id",
                        column: x => x.guild_id,
                        principalTable: "guilds",
                        principalColumn: "guild_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "premium_guild_subscriptions",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    discord_guild_id = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    purchaser_discord_user_id = table.Column<decimal>(type: "numeric(20,0)", nullable: true),
                    purchaser_last_fm_user_name = table.Column<string>(type: "text", nullable: true),
                    stripe_customer_id = table.Column<string>(type: "text", nullable: true),
                    stripe_subscription_id = table.Column<string>(type: "text", nullable: true),
                    discord_entitlement_id = table.Column<decimal>(type: "numeric(20,0)", nullable: true),
                    date_started = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    date_ending = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    entitlement_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    currency = table.Column<string>(type: "text", nullable: true),
                    purchase_source = table.Column<string>(type: "text", nullable: true),
                    welcome_message_sent = table.Column<bool>(type: "boolean", nullable: false),
                    times_transferred = table.Column<int>(type: "integer", nullable: true),
                    last_time_transferred = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_premium_guild_subscriptions", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_guild_featured_logs_guild_id_date_time",
                table: "guild_featured_logs",
                columns: new[] { "guild_id", "date_time" });

            migrationBuilder.CreateIndex(
                name: "ix_premium_guild_subscriptions_discord_entitlement_id",
                table: "premium_guild_subscriptions",
                column: "discord_entitlement_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_premium_guild_subscriptions_discord_guild_id",
                table: "premium_guild_subscriptions",
                column: "discord_guild_id");

            migrationBuilder.CreateIndex(
                name: "ix_premium_guild_subscriptions_stripe_subscription_id",
                table: "premium_guild_subscriptions",
                column: "stripe_subscription_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "guild_featured_logs");

            migrationBuilder.DropTable(
                name: "premium_guild_subscriptions");

            migrationBuilder.DropColumn(
                name: "featured_mode",
                table: "guilds");

            migrationBuilder.DropColumn(
                name: "last_recap",
                table: "guilds");

            migrationBuilder.DropColumn(
                name: "recap_channel_id",
                table: "guilds");

            migrationBuilder.DropColumn(
                name: "recap_schedule",
                table: "guilds");
        }
    }
}
