using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace FMBot.Persistence.EntityFrameWork.Migrations
{
    /// <inheritdoc />
    public partial class AddLyrics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "lyrics_date",
                table: "tracks",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "plain_lyrics",
                table: "tracks",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "track_synced_lyrics",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    track_id = table.Column<int>(type: "integer", nullable: false),
                    timestamp = table.Column<TimeSpan>(type: "interval", nullable: false),
                    text = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_track_synced_lyrics", x => x.id);
                    table.ForeignKey(
                        name: "fk_track_synced_lyrics_tracks_track_id",
                        column: x => x.track_id,
                        principalTable: "tracks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_track_synced_lyrics_track_id",
                table: "track_synced_lyrics",
                column: "track_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "track_synced_lyrics");

            migrationBuilder.DropColumn(
                name: "lyrics_date",
                table: "tracks");

            migrationBuilder.DropColumn(
                name: "plain_lyrics",
                table: "tracks");
        }
    }
}
