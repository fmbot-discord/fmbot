using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FMBot.Persistence.EntityFrameWork.Migrations
{
    /// <inheritdoc />
    public partial class ExpandStripeOptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "purchase_source",
                table: "stripe_supporters",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "bye_promo",
                table: "stripe_pricing",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "bye_promo_sub_text",
                table: "stripe_pricing",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "lifetime_price_id",
                table: "stripe_pricing",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "lifetime_price_string",
                table: "stripe_pricing",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "lifetime_sub_text",
                table: "stripe_pricing",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "lifetime_summary",
                table: "stripe_pricing",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "purchase_source",
                table: "stripe_supporters");

            migrationBuilder.DropColumn(
                name: "bye_promo",
                table: "stripe_pricing");

            migrationBuilder.DropColumn(
                name: "bye_promo_sub_text",
                table: "stripe_pricing");

            migrationBuilder.DropColumn(
                name: "lifetime_price_id",
                table: "stripe_pricing");

            migrationBuilder.DropColumn(
                name: "lifetime_price_string",
                table: "stripe_pricing");

            migrationBuilder.DropColumn(
                name: "lifetime_sub_text",
                table: "stripe_pricing");

            migrationBuilder.DropColumn(
                name: "lifetime_summary",
                table: "stripe_pricing");
        }
    }
}
