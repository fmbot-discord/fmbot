using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

namespace FMBot.Persistence.EntityFrameWork.Migrations
{
    public partial class AddArtistWithProperties : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "artists",
                columns: table => new
                {
                    id = table.Column<int>(nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(nullable: true),
                    last_fm_url = table.Column<string>(nullable: true),
                    mbid = table.Column<Guid>(nullable: true),
                    spotify_image_url = table.Column<string>(nullable: true),
                    spotify_image_date = table.Column<DateTime>(nullable: true),
                    spotify_id = table.Column<string>(nullable: true),
                    aliases = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_artists", x => x.id);
                });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "artists");
        }
    }
}
