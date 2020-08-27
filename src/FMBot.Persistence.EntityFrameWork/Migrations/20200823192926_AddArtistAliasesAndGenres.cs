using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

namespace FMBot.Persistence.EntityFrameWork.Migrations
{
    public partial class AddArtistAliasesAndGenres : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "genres",
                table: "artists");

            migrationBuilder.CreateTable(
                name: "artist_aliases",
                columns: table => new
                {
                    id = table.Column<int>(nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    artist_id = table.Column<int>(nullable: false),
                    alias = table.Column<string>(nullable: true),
                    corrects_in_scrobbles = table.Column<bool>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_artist_aliases", x => x.id);
                    table.ForeignKey(
                        name: "fk_artist_aliases_artists_artist_id",
                        column: x => x.artist_id,
                        principalTable: "artists",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "artist_genres",
                columns: table => new
                {
                    id = table.Column<int>(nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    artist_id = table.Column<int>(nullable: false),
                    name = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_artist_genres", x => x.id);
                    table.ForeignKey(
                        name: "fk_artist_genres_artists_artist_id",
                        column: x => x.artist_id,
                        principalTable: "artists",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_artist_aliases_artist_id",
                table: "artist_aliases",
                column: "artist_id");

            migrationBuilder.CreateIndex(
                name: "ix_artist_genres_artist_id",
                table: "artist_genres",
                column: "artist_id");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "artist_aliases");

            migrationBuilder.DropTable(
                name: "artist_genres");

            migrationBuilder.AddColumn<string>(
                name: "genres",
                table: "artists",
                type: "text",
                nullable: true);
        }
    }
}
