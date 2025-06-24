using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FMBot.Persistence.EntityFrameWork.Migrations
{
    /// <inheritdoc />
    public partial class AddSupporterGifting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "gift_receiver_last_fm_user_name",
                table: "stripe_supporters",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "quarterly_price_id",
                table: "stripe_pricing",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "quarterly_price_string",
                table: "stripe_pricing",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "quarterly_sub_text",
                table: "stripe_pricing",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "quarterly_summary",
                table: "stripe_pricing",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "two_year_price_id",
                table: "stripe_pricing",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "two_year_price_string",
                table: "stripe_pricing",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "two_year_sub_text",
                table: "stripe_pricing",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "two_year_summary",
                table: "stripe_pricing",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "gift_receiver_last_fm_user_name",
                table: "stripe_supporters");

            migrationBuilder.DropColumn(
                name: "quarterly_price_id",
                table: "stripe_pricing");

            migrationBuilder.DropColumn(
                name: "quarterly_price_string",
                table: "stripe_pricing");

            migrationBuilder.DropColumn(
                name: "quarterly_sub_text",
                table: "stripe_pricing");

            migrationBuilder.DropColumn(
                name: "quarterly_summary",
                table: "stripe_pricing");

            migrationBuilder.DropColumn(
                name: "two_year_price_id",
                table: "stripe_pricing");

            migrationBuilder.DropColumn(
                name: "two_year_price_string",
                table: "stripe_pricing");

            migrationBuilder.DropColumn(
                name: "two_year_sub_text",
                table: "stripe_pricing");

            migrationBuilder.DropColumn(
                name: "two_year_summary",
                table: "stripe_pricing");
        }
    }
}
