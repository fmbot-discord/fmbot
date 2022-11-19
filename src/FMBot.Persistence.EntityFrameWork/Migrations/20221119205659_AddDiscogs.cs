using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace FMBot.Persistence.EntityFrameWork.Migrations
{
    /// <inheritdoc />
    public partial class AddDiscogs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "discogs_master",
                columns: table => new
                {
                    discogsid = table.Column<int>(name: "discogs_id", type: "integer", nullable: false),
                    title = table.Column<string>(type: "citext", nullable: true),
                    artist = table.Column<string>(type: "citext", nullable: true),
                    artistid = table.Column<int>(name: "artist_id", type: "integer", nullable: true),
                    artistdiscogsid = table.Column<int>(name: "artist_discogs_id", type: "integer", nullable: false),
                    featuringartistjoin = table.Column<string>(name: "featuring_artist_join", type: "text", nullable: true),
                    featuringartist = table.Column<string>(name: "featuring_artist", type: "citext", nullable: true),
                    featuringartistid = table.Column<int>(name: "featuring_artist_id", type: "integer", nullable: true),
                    featuringartistdiscogsid = table.Column<int>(name: "featuring_artist_discogs_id", type: "integer", nullable: true),
                    country = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_discogs_master", x => x.discogsid);
                });

            migrationBuilder.CreateTable(
                name: "user_discogs",
                columns: table => new
                {
                    userid = table.Column<int>(name: "user_id", type: "integer", nullable: false),
                    discogsid = table.Column<int>(name: "discogs_id", type: "integer", nullable: false),
                    username = table.Column<string>(type: "text", nullable: true),
                    accesstoken = table.Column<string>(name: "access_token", type: "text", nullable: true),
                    accesstokensecret = table.Column<string>(name: "access_token_secret", type: "text", nullable: true),
                    minimumvalue = table.Column<string>(name: "minimum_value", type: "text", nullable: true),
                    medianvalue = table.Column<string>(name: "median_value", type: "text", nullable: true),
                    maximumvalue = table.Column<string>(name: "maximum_value", type: "text", nullable: true),
                    lastupdated = table.Column<DateTime>(name: "last_updated", type: "timestamp with time zone", nullable: true),
                    releaseslastupdated = table.Column<DateTime>(name: "releases_last_updated", type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_discogs", x => x.userid);
                    table.ForeignKey(
                        name: "fk_user_discogs_users_user_id",
                        column: x => x.userid,
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "discogs_genre",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    masterid = table.Column<int>(name: "master_id", type: "integer", nullable: false),
                    description = table.Column<string>(type: "citext", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_discogs_genre", x => x.id);
                    table.ForeignKey(
                        name: "fk_discogs_genre_discogs_master_discogs_master_temp_id1",
                        column: x => x.masterid,
                        principalTable: "discogs_master",
                        principalColumn: "discogs_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "discogs_release",
                columns: table => new
                {
                    discogsid = table.Column<int>(name: "discogs_id", type: "integer", nullable: false),
                    masterid = table.Column<int>(name: "master_id", type: "integer", nullable: false),
                    coverurl = table.Column<string>(name: "cover_url", type: "text", nullable: true),
                    year = table.Column<int>(type: "integer", nullable: true),
                    format = table.Column<string>(type: "citext", nullable: true),
                    formattext = table.Column<string>(name: "format_text", type: "text", nullable: true),
                    label = table.Column<string>(type: "citext", nullable: true),
                    secondlabel = table.Column<string>(name: "second_label", type: "text", nullable: true),
                    lowestprice = table.Column<decimal>(name: "lowest_price", type: "numeric", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_discogs_release", x => x.discogsid);
                    table.ForeignKey(
                        name: "fk_discogs_release_discogs_master_discogs_master_temp_id",
                        column: x => x.masterid,
                        principalTable: "discogs_master",
                        principalColumn: "discogs_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "discogs_style",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    masterid = table.Column<int>(name: "master_id", type: "integer", nullable: false),
                    description = table.Column<string>(type: "citext", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_discogs_style", x => x.id);
                    table.ForeignKey(
                        name: "fk_discogs_style_discogs_master_discogs_master_temp_id2",
                        column: x => x.masterid,
                        principalTable: "discogs_master",
                        principalColumn: "discogs_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "discogs_format_descriptions",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    releaseid = table.Column<int>(name: "release_id", type: "integer", nullable: false),
                    description = table.Column<string>(type: "citext", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_discogs_format_descriptions", x => x.id);
                    table.ForeignKey(
                        name: "fk_discogs_format_descriptions_discogs_release_discogs_release",
                        column: x => x.releaseid,
                        principalTable: "discogs_release",
                        principalColumn: "discogs_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_discogs_releases",
                columns: table => new
                {
                    userdiscogsreleaseid = table.Column<int>(name: "user_discogs_release_id", type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    userid = table.Column<int>(name: "user_id", type: "integer", nullable: false),
                    instanceid = table.Column<int>(name: "instance_id", type: "integer", nullable: false),
                    dateadded = table.Column<DateTime>(name: "date_added", type: "timestamp with time zone", nullable: false),
                    rating = table.Column<int>(type: "integer", nullable: true),
                    quantity = table.Column<string>(type: "text", nullable: true),
                    releaseid = table.Column<int>(name: "release_id", type: "integer", nullable: false),
                    userdiscogsuserid = table.Column<int>(name: "user_discogs_user_id", type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_discogs_releases", x => x.userdiscogsreleaseid);
                    table.ForeignKey(
                        name: "fk_user_discogs_releases_discogs_release_release_id",
                        column: x => x.releaseid,
                        principalTable: "discogs_release",
                        principalColumn: "discogs_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_user_discogs_releases_user_discogs_user_discogs_temp_id1",
                        column: x => x.userdiscogsuserid,
                        principalTable: "user_discogs",
                        principalColumn: "user_id");
                    table.ForeignKey(
                        name: "fk_user_discogs_releases_users_user_id",
                        column: x => x.userid,
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_discogs_format_descriptions_release_id",
                table: "discogs_format_descriptions",
                column: "release_id");

            migrationBuilder.CreateIndex(
                name: "ix_discogs_genre_master_id",
                table: "discogs_genre",
                column: "master_id");

            migrationBuilder.CreateIndex(
                name: "ix_discogs_release_master_id",
                table: "discogs_release",
                column: "master_id");

            migrationBuilder.CreateIndex(
                name: "ix_discogs_style_master_id",
                table: "discogs_style",
                column: "master_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_discogs_releases_release_id",
                table: "user_discogs_releases",
                column: "release_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_discogs_releases_user_discogs_user_id",
                table: "user_discogs_releases",
                column: "user_discogs_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_discogs_releases_user_id",
                table: "user_discogs_releases",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "discogs_format_descriptions");

            migrationBuilder.DropTable(
                name: "discogs_genre");

            migrationBuilder.DropTable(
                name: "discogs_style");

            migrationBuilder.DropTable(
                name: "user_discogs_releases");

            migrationBuilder.DropTable(
                name: "discogs_release");

            migrationBuilder.DropTable(
                name: "user_discogs");

            migrationBuilder.DropTable(
                name: "discogs_master");
        }
    }
}
