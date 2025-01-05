using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace FMBot.Persistence.EntityFrameWork.Migrations
{
    /// <inheritdoc />
    public partial class AddStripeLocalPricing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "stripe_pricing",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    @default = table.Column<bool>(name: "default", type: "boolean", nullable: false),
                    locales = table.Column<string>(type: "text", nullable: true),
                    type = table.Column<int>(type: "integer", nullable: false),
                    monthly_price_id = table.Column<string>(type: "text", nullable: true),
                    monthly_price_string = table.Column<string>(type: "text", nullable: true),
                    monthly_sub_text = table.Column<string>(type: "text", nullable: true),
                    monthly_summary = table.Column<string>(type: "text", nullable: true),
                    yearly_price_id = table.Column<string>(type: "text", nullable: true),
                    yearly_price_string = table.Column<string>(type: "text", nullable: true),
                    yearly_sub_text = table.Column<string>(type: "text", nullable: true),
                    yearly_summary = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stripe_pricing", x => x.id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "stripe_pricing");
        }
    }
}
