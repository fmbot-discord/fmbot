using Microsoft.EntityFrameworkCore.Migrations;

namespace FMBot.Persistence.EntityFrameWork.Migrations
{
    public partial class AddIndexes : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_guilds_guild_id",
                table: "guilds");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:citext", ",,")
                .Annotation("Npgsql:PostgresExtension:pg_trgm", ",,");

            migrationBuilder.AlterColumn<string>(
                name: "name",
                table: "user_tracks",
                type: "citext",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "artist_name",
                table: "user_tracks",
                type: "citext",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "track_name",
                table: "user_plays",
                type: "citext",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "artist_name",
                table: "user_plays",
                type: "citext",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "album_name",
                table: "user_plays",
                type: "citext",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "name",
                table: "user_artists",
                type: "citext",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "name",
                table: "user_albums",
                type: "citext",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "artist_name",
                table: "user_albums",
                type: "citext",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_users_discord_user_id",
                table: "users",
                column: "discord_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_guilds_discord_guild_id",
                table: "guilds",
                column: "discord_guild_id");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_users_discord_user_id",
                table: "users");

            migrationBuilder.DropIndex(
                name: "ix_guilds_discord_guild_id",
                table: "guilds");

            migrationBuilder.AlterDatabase()
                .OldAnnotation("Npgsql:PostgresExtension:citext", ",,")
                .OldAnnotation("Npgsql:PostgresExtension:pg_trgm", ",,");

            migrationBuilder.AlterColumn<string>(
                name: "name",
                table: "user_tracks",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "citext",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "artist_name",
                table: "user_tracks",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "citext",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "track_name",
                table: "user_plays",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "citext",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "artist_name",
                table: "user_plays",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "citext",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "album_name",
                table: "user_plays",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "citext",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "name",
                table: "user_artists",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "citext",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "name",
                table: "user_albums",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "citext",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "artist_name",
                table: "user_albums",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "citext",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_guilds_guild_id",
                table: "guilds",
                column: "guild_id");
        }
    }
}
