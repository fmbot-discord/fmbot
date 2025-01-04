using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace FMBot.Persistence.EntityFrameWork.Migrations
{
    /// <inheritdoc />
    public partial class AddStripeSupporters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "stripe_supporters",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    purchaser_discord_user_id = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    purchaser_last_fm_user_name = table.Column<string>(type: "text", nullable: true),
                    gift_receiver_discord_user_id = table.Column<decimal>(type: "numeric(20,0)", nullable: true),
                    stripe_customer_id = table.Column<string>(type: "text", nullable: true),
                    stripe_subscription_id = table.Column<string>(type: "text", nullable: true),
                    date_started = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    date_ending = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    entitlement_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    times_transferred = table.Column<int>(type: "integer", nullable: true),
                    last_time_transferred = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    type = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stripe_supporters", x => x.id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "stripe_supporters");
        }
    }
}
