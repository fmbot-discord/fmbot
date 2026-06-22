using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FMBot.Persistence.EntityFrameWork.Migrations
{
    /// <inheritdoc />
    public partial class NormalizeTags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"CREATE OR REPLACE FUNCTION tag_normalize(input text) RETURNS text
LANGUAGE sql IMMUTABLE PARALLEL SAFE STRICT AS
$$ SELECT COALESCE(NULLIF(regexp_replace(lower(input), '[[:space:]_-]+', '', 'g'), ''), lower(input)) $$;");
            migrationBuilder.Sql("TRUNCATE public.tags CASCADE;");

            migrationBuilder.DropIndex(
                name: "ix_tags_name",
                table: "tags");

            migrationBuilder.AddColumn<string>(
                name: "normalized_name",
                table: "tags",
                type: "text",
                nullable: true,
                computedColumnSql: "tag_normalize(name::text)",
                stored: true);

            migrationBuilder.CreateIndex(
                name: "ix_tags_normalized_name",
                table: "tags",
                column: "normalized_name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_tags_normalized_name",
                table: "tags");

            migrationBuilder.DropColumn(
                name: "normalized_name",
                table: "tags");

            migrationBuilder.CreateIndex(
                name: "ix_tags_name",
                table: "tags",
                column: "name",
                unique: true);

            migrationBuilder.Sql("DROP FUNCTION IF EXISTS tag_normalize(text);");
        }
    }
}
