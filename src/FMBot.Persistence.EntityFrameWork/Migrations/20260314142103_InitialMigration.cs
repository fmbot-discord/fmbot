using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace FMBot.Persistence.EntityFrameWork.Migrations
{
    /// <inheritdoc />
    public partial class InitialMigration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:citext", ",,")
                .Annotation("Npgsql:PostgresExtension:hstore", ",,")
                .Annotation("Npgsql:PostgresExtension:pg_trgm", ",,");

            migrationBuilder.CreateTable(
                name: "ai_prompts",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    type = table.Column<int>(type: "integer", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    language = table.Column<string>(type: "text", nullable: true),
                    prompt = table.Column<string>(type: "text", nullable: true),
                    free_model = table.Column<string>(type: "text", nullable: true),
                    premium_model = table.Column<string>(type: "text", nullable: true),
                    ultra_model = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ai_prompts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "artists",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "citext", nullable: true),
                    last_fm_url = table.Column<string>(type: "text", nullable: true),
                    last_fm_description = table.Column<string>(type: "text", nullable: true),
                    lastfm_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    spotify_image_url = table.Column<string>(type: "text", nullable: true),
                    spotify_image_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    spotify_id = table.Column<string>(type: "text", nullable: true),
                    apple_music_id = table.Column<int>(type: "integer", nullable: true),
                    popularity = table.Column<int>(type: "integer", nullable: true),
                    mbid = table.Column<Guid>(type: "uuid", nullable: true),
                    music_brainz_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    location = table.Column<string>(type: "text", nullable: true),
                    country_code = table.Column<string>(type: "text", nullable: true),
                    type = table.Column<string>(type: "text", nullable: true),
                    gender = table.Column<string>(type: "text", nullable: true),
                    disambiguation = table.Column<string>(type: "text", nullable: true),
                    start_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    end_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    apple_music_url = table.Column<string>(type: "text", nullable: true),
                    apple_music_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_artists", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "botted_user_report",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_name_last_fm = table.Column<string>(type: "text", nullable: true),
                    provided_note = table.Column<string>(type: "text", nullable: true),
                    report_status = table.Column<int>(type: "integer", nullable: false),
                    reported_by_discord_user_id = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    processed_by_discord_user_id = table.Column<decimal>(type: "numeric(20,0)", nullable: true),
                    reported_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    processed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_botted_user_report", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "botted_users",
                columns: table => new
                {
                    botted_user_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_name_last_fm = table.Column<string>(type: "text", nullable: true),
                    last_fm_registered = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    notes = table.Column<string>(type: "text", nullable: true),
                    ban_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_botted_users", x => x.botted_user_id);
                });

            migrationBuilder.CreateTable(
                name: "censored_music",
                columns: table => new
                {
                    censored_music_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    artist_name = table.Column<string>(type: "text", nullable: true),
                    album_name = table.Column<string>(type: "text", nullable: true),
                    alternative_cover_url = table.Column<string>(type: "text", nullable: true),
                    times_censored = table.Column<int>(type: "integer", nullable: true),
                    safe_for_commands = table.Column<bool>(type: "boolean", nullable: false),
                    safe_for_featured = table.Column<bool>(type: "boolean", nullable: false),
                    featured_ban_only = table.Column<bool>(type: "boolean", nullable: true),
                    artist = table.Column<bool>(type: "boolean", nullable: false),
                    censor_type = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_censored_music", x => x.censored_music_id);
                });

            migrationBuilder.CreateTable(
                name: "discogs_releases",
                columns: table => new
                {
                    discogs_id = table.Column<int>(type: "integer", nullable: false),
                    master_id = table.Column<int>(type: "integer", nullable: true),
                    album_id = table.Column<int>(type: "integer", nullable: true),
                    title = table.Column<string>(type: "citext", nullable: true),
                    artist = table.Column<string>(type: "citext", nullable: true),
                    artist_id = table.Column<int>(type: "integer", nullable: true),
                    artist_discogs_id = table.Column<int>(type: "integer", nullable: false),
                    featuring_artist_join = table.Column<string>(type: "text", nullable: true),
                    featuring_artist = table.Column<string>(type: "citext", nullable: true),
                    featuring_artist_id = table.Column<int>(type: "integer", nullable: true),
                    featuring_artist_discogs_id = table.Column<int>(type: "integer", nullable: true),
                    cover_url = table.Column<string>(type: "text", nullable: true),
                    year = table.Column<int>(type: "integer", nullable: true),
                    format = table.Column<string>(type: "citext", nullable: true),
                    format_text = table.Column<string>(type: "text", nullable: true),
                    label = table.Column<string>(type: "citext", nullable: true),
                    second_label = table.Column<string>(type: "text", nullable: true),
                    lowest_price = table.Column<decimal>(type: "numeric", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_discogs_releases", x => x.discogs_id);
                });

            migrationBuilder.CreateTable(
                name: "global_filtered_users",
                columns: table => new
                {
                    global_filtered_user_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_name_last_fm = table.Column<string>(type: "text", nullable: true),
                    registered_last_fm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    user_id = table.Column<int>(type: "integer", nullable: true),
                    reason = table.Column<int>(type: "integer", nullable: false),
                    reason_amount = table.Column<int>(type: "integer", nullable: true),
                    occurrence_start = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    occurrence_end = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    month_length = table.Column<int>(type: "integer", nullable: true),
                    created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_global_filtered_users", x => x.global_filtered_user_id);
                });

            migrationBuilder.CreateTable(
                name: "guilds",
                columns: table => new
                {
                    guild_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    discord_guild_id = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    name = table.Column<string>(type: "text", nullable: true),
                    prefix = table.Column<string>(type: "text", nullable: true),
                    fm_embed_type = table.Column<int>(type: "integer", nullable: true),
                    emote_reactions = table.Column<string>(type: "text", nullable: true),
                    disabled_commands = table.Column<string>(type: "text", nullable: true),
                    last_indexed = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    special_guild = table.Column<bool>(type: "boolean", nullable: true),
                    disable_supporter_messages = table.Column<bool>(type: "boolean", nullable: true),
                    activity_threshold_days = table.Column<int>(type: "integer", nullable: true),
                    crowns_activity_threshold_days = table.Column<int>(type: "integer", nullable: true),
                    crowns_minimum_playcount_threshold = table.Column<int>(type: "integer", nullable: true),
                    crowns_disabled = table.Column<bool>(type: "boolean", nullable: true),
                    allowed_roles = table.Column<decimal[]>(type: "numeric(20,0)[]", nullable: true),
                    blocked_roles = table.Column<decimal[]>(type: "numeric(20,0)[]", nullable: true),
                    bot_management_roles = table.Column<decimal[]>(type: "numeric(20,0)[]", nullable: true),
                    filter_roles = table.Column<decimal[]>(type: "numeric(20,0)[]", nullable: true),
                    user_activity_threshold_days = table.Column<int>(type: "integer", nullable: true),
                    custom_logo = table.Column<string>(type: "text", nullable: true),
                    last_crown_seed = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    automatic_crown_seeder = table.Column<int>(type: "integer", nullable: true),
                    guild_flags = table.Column<long>(type: "bigint", nullable: true),
                    who_knows_whitelist_role_id = table.Column<decimal>(type: "numeric(20,0)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_guilds", x => x.guild_id);
                });

            migrationBuilder.CreateTable(
                name: "jumble_sessions",
                columns: table => new
                {
                    jumble_session_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    starter_user_id = table.Column<int>(type: "integer", nullable: false),
                    discord_guild_id = table.Column<decimal>(type: "numeric(20,0)", nullable: true),
                    discord_channel_id = table.Column<decimal>(type: "numeric(20,0)", nullable: true),
                    discord_id = table.Column<decimal>(type: "numeric(20,0)", nullable: true),
                    discord_response_id = table.Column<decimal>(type: "numeric(20,0)", nullable: true),
                    jumble_type = table.Column<int>(type: "integer", nullable: false),
                    date_started = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    date_ended = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    reshuffles = table.Column<int>(type: "integer", nullable: false),
                    jumbled_artist = table.Column<string>(type: "text", nullable: true),
                    correct_answer = table.Column<string>(type: "text", nullable: true),
                    artist_name = table.Column<string>(type: "text", nullable: true),
                    album_name = table.Column<string>(type: "text", nullable: true),
                    blur_level = table.Column<float>(type: "real", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_jumble_sessions", x => x.jumble_session_id);
                });

            migrationBuilder.CreateTable(
                name: "stripe_pricing",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    @default = table.Column<bool>(name: "default", type: "boolean", nullable: false),
                    locales = table.Column<string>(type: "text", nullable: true),
                    type = table.Column<int>(type: "integer", nullable: false),
                    currency = table.Column<string>(type: "text", nullable: true),
                    monthly_price_id = table.Column<string>(type: "text", nullable: true),
                    monthly_price_string = table.Column<string>(type: "text", nullable: true),
                    monthly_sub_text = table.Column<string>(type: "text", nullable: true),
                    monthly_summary = table.Column<string>(type: "text", nullable: true),
                    quarterly_price_id = table.Column<string>(type: "text", nullable: true),
                    quarterly_price_string = table.Column<string>(type: "text", nullable: true),
                    quarterly_sub_text = table.Column<string>(type: "text", nullable: true),
                    quarterly_summary = table.Column<string>(type: "text", nullable: true),
                    yearly_price_id = table.Column<string>(type: "text", nullable: true),
                    yearly_price_string = table.Column<string>(type: "text", nullable: true),
                    yearly_sub_text = table.Column<string>(type: "text", nullable: true),
                    yearly_summary = table.Column<string>(type: "text", nullable: true),
                    two_year_price_id = table.Column<string>(type: "text", nullable: true),
                    two_year_price_string = table.Column<string>(type: "text", nullable: true),
                    two_year_sub_text = table.Column<string>(type: "text", nullable: true),
                    two_year_summary = table.Column<string>(type: "text", nullable: true),
                    lifetime_price_id = table.Column<string>(type: "text", nullable: true),
                    lifetime_price_string = table.Column<string>(type: "text", nullable: true),
                    lifetime_sub_text = table.Column<string>(type: "text", nullable: true),
                    lifetime_summary = table.Column<string>(type: "text", nullable: true),
                    bye_promo = table.Column<string>(type: "text", nullable: true),
                    bye_promo_sub_text = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stripe_pricing", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "stripe_supporters",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    purchaser_discord_user_id = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    purchaser_last_fm_user_name = table.Column<string>(type: "text", nullable: true),
                    gift_receiver_discord_user_id = table.Column<decimal>(type: "numeric(20,0)", nullable: true),
                    gift_receiver_last_fm_user_name = table.Column<string>(type: "text", nullable: true),
                    stripe_customer_id = table.Column<string>(type: "text", nullable: true),
                    stripe_subscription_id = table.Column<string>(type: "text", nullable: true),
                    date_started = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    date_ending = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    entitlement_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    times_transferred = table.Column<int>(type: "integer", nullable: true),
                    last_time_transferred = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    currency = table.Column<string>(type: "text", nullable: true),
                    type = table.Column<int>(type: "integer", nullable: false),
                    purchase_source = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stripe_supporters", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "supporters",
                columns: table => new
                {
                    supporter_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "text", nullable: true),
                    supporter_type = table.Column<int>(type: "integer", nullable: false),
                    notes = table.Column<string>(type: "text", nullable: true),
                    supporter_messages_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    visible_in_overview = table.Column<bool>(type: "boolean", nullable: false),
                    subscription_type = table.Column<int>(type: "integer", nullable: true),
                    open_collective_id = table.Column<string>(type: "text", nullable: true),
                    last_payment = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    expired = table.Column<bool>(type: "boolean", nullable: true),
                    discord_user_id = table.Column<decimal>(type: "numeric(20,0)", nullable: true),
                    created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    modified = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_supporters", x => x.supporter_id);
                });

            migrationBuilder.CreateTable(
                name: "user_tokens",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    discord_user_id = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    last_updated = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    bot_type = table.Column<int>(type: "integer", nullable: false),
                    scope = table.Column<string>(type: "text", nullable: true),
                    access_token = table.Column<string>(type: "text", nullable: true),
                    refresh_token = table.Column<string>(type: "text", nullable: true),
                    token_expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_tokens", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    user_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    discord_user_id = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    blocked = table.Column<bool>(type: "boolean", nullable: true),
                    user_type = table.Column<int>(type: "integer", nullable: false),
                    data_source = table.Column<int>(type: "integer", nullable: false),
                    user_name_last_fm = table.Column<string>(type: "text", nullable: true),
                    session_key_last_fm = table.Column<string>(type: "text", nullable: true),
                    lastfm_pro = table.Column<bool>(type: "boolean", nullable: true),
                    registered_last_fm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    total_playcount = table.Column<long>(type: "bigint", nullable: true),
                    rym_enabled = table.Column<bool>(type: "boolean", nullable: true),
                    music_bot_tracking_disabled = table.Column<bool>(type: "boolean", nullable: true),
                    fm_embed_type = table.Column<int>(type: "integer", nullable: false),
                    fm_footer_options = table.Column<long>(type: "bigint", nullable: false),
                    mode = table.Column<int>(type: "integer", nullable: true),
                    who_knows_mode = table.Column<int>(type: "integer", nullable: true),
                    privacy_level = table.Column<int>(type: "integer", nullable: false),
                    last_indexed = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_small_indexed = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_updated = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_scrobble_update = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_used = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    emote_reactions = table.Column<string>(type: "text", nullable: true),
                    time_zone = table.Column<string>(type: "text", nullable: true),
                    number_format = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_users", x => x.user_id);
                });

            migrationBuilder.CreateTable(
                name: "albums",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "citext", nullable: true),
                    artist_name = table.Column<string>(type: "citext", nullable: true),
                    last_fm_url = table.Column<string>(type: "text", nullable: true),
                    last_fm_description = table.Column<string>(type: "text", nullable: true),
                    lastfm_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    mbid = table.Column<Guid>(type: "uuid", nullable: true),
                    upc = table.Column<string>(type: "text", nullable: true),
                    type = table.Column<string>(type: "text", nullable: true),
                    spotify_image_url = table.Column<string>(type: "text", nullable: true),
                    spotify_image_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    lastfm_image_url = table.Column<string>(type: "text", nullable: true),
                    spotify_id = table.Column<string>(type: "text", nullable: true),
                    apple_music_id = table.Column<int>(type: "integer", nullable: true),
                    popularity = table.Column<int>(type: "integer", nullable: true),
                    label = table.Column<string>(type: "text", nullable: true),
                    apple_music_url = table.Column<string>(type: "text", nullable: true),
                    apple_music_tagline = table.Column<string>(type: "text", nullable: true),
                    apple_music_description = table.Column<string>(type: "text", nullable: true),
                    apple_music_short_description = table.Column<string>(type: "text", nullable: true),
                    apple_music_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    release_date = table.Column<string>(type: "text", nullable: true),
                    release_date_precision = table.Column<string>(type: "text", nullable: true),
                    artist_id = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_albums", x => x.id);
                    table.ForeignKey(
                        name: "fk_albums_artists_artist_id",
                        column: x => x.artist_id,
                        principalTable: "artists",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "artist_aliases",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    artist_id = table.Column<int>(type: "integer", nullable: false),
                    alias = table.Column<string>(type: "text", nullable: true),
                    corrects_in_scrobbles = table.Column<bool>(type: "boolean", nullable: false),
                    options = table.Column<int>(type: "integer", nullable: false)
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
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    artist_id = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "citext", nullable: true)
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

            migrationBuilder.CreateTable(
                name: "artist_images",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    artist_id = table.Column<int>(type: "integer", nullable: false),
                    image_source = table.Column<int>(type: "integer", nullable: false),
                    image_type = table.Column<int>(type: "integer", nullable: false),
                    url = table.Column<string>(type: "text", nullable: true),
                    last_updated = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    width = table.Column<int>(type: "integer", nullable: true),
                    height = table.Column<int>(type: "integer", nullable: true),
                    bg_color = table.Column<string>(type: "text", nullable: true),
                    text_color1 = table.Column<string>(type: "text", nullable: true),
                    text_color2 = table.Column<string>(type: "text", nullable: true),
                    text_color3 = table.Column<string>(type: "text", nullable: true),
                    text_color4 = table.Column<string>(type: "text", nullable: true),
                    preview_frame_url = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_artist_images", x => x.id);
                    table.ForeignKey(
                        name: "fk_artist_images_artists_artist_id",
                        column: x => x.artist_id,
                        principalTable: "artists",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "artist_links",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    artist_id = table.Column<int>(type: "integer", nullable: false),
                    url = table.Column<string>(type: "text", nullable: true),
                    username = table.Column<string>(type: "text", nullable: true),
                    type = table.Column<int>(type: "integer", nullable: false),
                    manually_added = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_artist_links", x => x.id);
                    table.ForeignKey(
                        name: "fk_artist_links_artists_artist_id",
                        column: x => x.artist_id,
                        principalTable: "artists",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "discogs_format_descriptions",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    release_id = table.Column<int>(type: "integer", nullable: false),
                    description = table.Column<string>(type: "citext", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_discogs_format_descriptions", x => x.id);
                    table.ForeignKey(
                        name: "fk_discogs_format_descriptions_discogs_releases_release_id",
                        column: x => x.release_id,
                        principalTable: "discogs_releases",
                        principalColumn: "discogs_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "discogs_genre",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    release_id = table.Column<int>(type: "integer", nullable: false),
                    description = table.Column<string>(type: "citext", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_discogs_genre", x => x.id);
                    table.ForeignKey(
                        name: "fk_discogs_genre_discogs_releases_release_id",
                        column: x => x.release_id,
                        principalTable: "discogs_releases",
                        principalColumn: "discogs_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "discogs_style",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    release_id = table.Column<int>(type: "integer", nullable: false),
                    description = table.Column<string>(type: "citext", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_discogs_style", x => x.id);
                    table.ForeignKey(
                        name: "fk_discogs_style_discogs_releases_release_id",
                        column: x => x.release_id,
                        principalTable: "discogs_releases",
                        principalColumn: "discogs_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "channels",
                columns: table => new
                {
                    channel_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    discord_channel_id = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    name = table.Column<string>(type: "text", nullable: true),
                    guild_id = table.Column<int>(type: "integer", nullable: false),
                    disabled_commands = table.Column<string>(type: "text", nullable: true),
                    fm_cooldown = table.Column<int>(type: "integer", nullable: true),
                    bot_disabled = table.Column<bool>(type: "boolean", nullable: true),
                    fm_embed_type = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_channels", x => x.channel_id);
                    table.ForeignKey(
                        name: "fk_channels_guilds_guild_id",
                        column: x => x.guild_id,
                        principalTable: "guilds",
                        principalColumn: "guild_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "guild_shortcuts",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    guild_id = table.Column<int>(type: "integer", nullable: false),
                    input = table.Column<string>(type: "text", nullable: true),
                    output = table.Column<string>(type: "text", nullable: true),
                    created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    modified = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_guild_shortcuts", x => x.id);
                    table.ForeignKey(
                        name: "fk_guild_shortcuts_guilds_guild_id",
                        column: x => x.guild_id,
                        principalTable: "guilds",
                        principalColumn: "guild_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "webhooks",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    discord_webhook_id = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    discord_thread_id = table.Column<decimal>(type: "numeric(20,0)", nullable: true),
                    guild_id = table.Column<int>(type: "integer", nullable: false),
                    token = table.Column<string>(type: "text", nullable: true),
                    bot_type = table.Column<int>(type: "integer", nullable: false),
                    created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_webhooks", x => x.id);
                    table.ForeignKey(
                        name: "fk_webhooks_guilds_guild_id",
                        column: x => x.guild_id,
                        principalTable: "guilds",
                        principalColumn: "guild_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "jumble_session_answers",
                columns: table => new
                {
                    jumble_session_answer_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    jumble_session_id = table.Column<int>(type: "integer", nullable: false),
                    date_answered = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    discord_user_id = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    correct = table.Column<bool>(type: "boolean", nullable: false),
                    answer = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_jumble_session_answers", x => x.jumble_session_answer_id);
                    table.ForeignKey(
                        name: "fk_jumble_session_answers_jumble_sessions_jumble_session_id",
                        column: x => x.jumble_session_id,
                        principalTable: "jumble_sessions",
                        principalColumn: "jumble_session_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "jumble_session_hint",
                columns: table => new
                {
                    jumble_session_hint_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    jumble_session_id = table.Column<int>(type: "integer", nullable: false),
                    type = table.Column<int>(type: "integer", nullable: false),
                    content = table.Column<string>(type: "text", nullable: true),
                    hint_shown = table.Column<bool>(type: "boolean", nullable: false),
                    order = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_jumble_session_hint", x => x.jumble_session_hint_id);
                    table.ForeignKey(
                        name: "fk_jumble_session_hint_jumble_sessions_jumble_session_id",
                        column: x => x.jumble_session_id,
                        principalTable: "jumble_sessions",
                        principalColumn: "jumble_session_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ai_generations",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    discord_id = table.Column<decimal>(type: "numeric(20,0)", nullable: true),
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    targeted_user_id = table.Column<int>(type: "integer", nullable: true),
                    prompt = table.Column<string>(type: "text", nullable: true),
                    output = table.Column<string>(type: "text", nullable: true),
                    model = table.Column<string>(type: "text", nullable: true),
                    total_tokens = table.Column<int>(type: "integer", nullable: false),
                    date_generated = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ai_generations", x => x.id);
                    table.ForeignKey(
                        name: "fk_ai_generations_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "featured_logs",
                columns: table => new
                {
                    featured_log_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    has_featured = table.Column<bool>(type: "boolean", nullable: false),
                    no_update = table.Column<bool>(type: "boolean", nullable: true),
                    user_id = table.Column<int>(type: "integer", nullable: true),
                    bot_type = table.Column<int>(type: "integer", nullable: false),
                    featured_mode = table.Column<int>(type: "integer", nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    image_url = table.Column<string>(type: "text", nullable: true),
                    track_name = table.Column<string>(type: "citext", nullable: true),
                    artist_name = table.Column<string>(type: "citext", nullable: true),
                    album_name = table.Column<string>(type: "citext", nullable: true),
                    date_time = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    supporter_day = table.Column<bool>(type: "boolean", nullable: false),
                    full_size_image = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "text", nullable: true),
                    reactions = table.Column<string[]>(type: "text[]", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_featured_logs", x => x.featured_log_id);
                    table.ForeignKey(
                        name: "fk_featured_logs_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "user_id");
                });

            migrationBuilder.CreateTable(
                name: "friends",
                columns: table => new
                {
                    friend_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    last_fm_user_name = table.Column<string>(type: "text", nullable: true),
                    friend_user_id = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_friends", x => x.friend_id);
                    table.ForeignKey(
                        name: "FK.Friends.Users_FriendUserID",
                        column: x => x.friend_user_id,
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK.Friends.Users_UserID",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "guild_blocked_users",
                columns: table => new
                {
                    guild_id = table.Column<int>(type: "integer", nullable: false),
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    blocked_from_crowns = table.Column<bool>(type: "boolean", nullable: false),
                    blocked_from_who_knows = table.Column<bool>(type: "boolean", nullable: false),
                    self_block_from_who_knows = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_guild_blocked_users", x => new { x.guild_id, x.user_id });
                    table.ForeignKey(
                        name: "fk_guild_blocked_users_guilds_guild_id",
                        column: x => x.guild_id,
                        principalTable: "guilds",
                        principalColumn: "guild_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_guild_blocked_users_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "guild_users",
                columns: table => new
                {
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    guild_id = table.Column<int>(type: "integer", nullable: false),
                    user_name = table.Column<string>(type: "text", nullable: true),
                    bot = table.Column<bool>(type: "boolean", nullable: true),
                    who_knows_whitelisted = table.Column<bool>(type: "boolean", nullable: true),
                    who_knows_blocked = table.Column<bool>(type: "boolean", nullable: true),
                    roles = table.Column<decimal[]>(type: "numeric(20,0)[]", nullable: true),
                    last_message = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_guild_users", x => new { x.guild_id, x.user_id });
                    table.ForeignKey(
                        name: "fk_guild_users_guilds_guild_id",
                        column: x => x.guild_id,
                        principalTable: "guilds",
                        principalColumn: "guild_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_guild_users_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "inactive_user_log",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    user_name_last_fm = table.Column<string>(type: "text", nullable: true),
                    response_status = table.Column<int>(type: "integer", nullable: false),
                    created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inactive_user_log", x => x.id);
                    table.ForeignKey(
                        name: "fk_inactive_user_log_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_albums",
                columns: table => new
                {
                    user_album_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "citext", nullable: true),
                    artist_name = table.Column<string>(type: "citext", nullable: true),
                    playcount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_albums", x => x.user_album_id);
                    table.ForeignKey(
                        name: "fk_user_albums_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_artists",
                columns: table => new
                {
                    user_artist_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "citext", nullable: true),
                    playcount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_artists", x => x.user_artist_id);
                    table.ForeignKey(
                        name: "fk_user_artists_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_crowns",
                columns: table => new
                {
                    crown_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    guild_id = table.Column<int>(type: "integer", nullable: false),
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    artist_name = table.Column<string>(type: "citext", nullable: true),
                    current_playcount = table.Column<int>(type: "integer", nullable: false),
                    start_playcount = table.Column<int>(type: "integer", nullable: false),
                    created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    modified = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    seeded_crown = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_crowns", x => x.crown_id);
                    table.ForeignKey(
                        name: "fk_user_crowns_guilds_guild_id",
                        column: x => x.guild_id,
                        principalTable: "guilds",
                        principalColumn: "guild_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_user_crowns_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_discogs",
                columns: table => new
                {
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    discogs_id = table.Column<int>(type: "integer", nullable: false),
                    username = table.Column<string>(type: "text", nullable: true),
                    access_token = table.Column<string>(type: "text", nullable: true),
                    access_token_secret = table.Column<string>(type: "text", nullable: true),
                    minimum_value = table.Column<string>(type: "text", nullable: true),
                    median_value = table.Column<string>(type: "text", nullable: true),
                    maximum_value = table.Column<string>(type: "text", nullable: true),
                    hide_value = table.Column<bool>(type: "boolean", nullable: true),
                    last_updated = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    releases_last_updated = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_discogs", x => x.user_id);
                    table.ForeignKey(
                        name: "fk_user_discogs_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_discogs_releases",
                columns: table => new
                {
                    user_discogs_release_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    instance_id = table.Column<int>(type: "integer", nullable: false),
                    date_added = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    rating = table.Column<int>(type: "integer", nullable: true),
                    quantity = table.Column<string>(type: "text", nullable: true),
                    release_id = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_discogs_releases", x => x.user_discogs_release_id);
                    table.ForeignKey(
                        name: "fk_user_discogs_releases_discogs_releases_release_id",
                        column: x => x.release_id,
                        principalTable: "discogs_releases",
                        principalColumn: "discogs_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_user_discogs_releases_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_fm_settings",
                columns: table => new
                {
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    embed_type = table.Column<int>(type: "integer", nullable: false),
                    small_text_type = table.Column<int>(type: "integer", nullable: true),
                    accent_color = table.Column<int>(type: "integer", nullable: true),
                    custom_color = table.Column<string>(type: "text", nullable: true),
                    buttons = table.Column<long>(type: "bigint", nullable: true),
                    private_button_response = table.Column<bool>(type: "boolean", nullable: true),
                    footer_options = table.Column<long>(type: "bigint", nullable: false),
                    modified = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_fm_settings", x => x.user_id);
                    table.ForeignKey(
                        name: "fk_user_fm_settings_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_interactions",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    discord_guild_id = table.Column<decimal>(type: "numeric(20,0)", nullable: true),
                    discord_channel_id = table.Column<decimal>(type: "numeric(20,0)", nullable: true),
                    discord_id = table.Column<decimal>(type: "numeric(20,0)", nullable: true),
                    discord_response_id = table.Column<decimal>(type: "numeric(20,0)", nullable: true),
                    type = table.Column<int>(type: "integer", nullable: false),
                    command_name = table.Column<string>(type: "text", nullable: true),
                    command_content = table.Column<string>(type: "text", nullable: true),
                    command_options = table.Column<Dictionary<string, string>>(type: "hstore", nullable: true),
                    response = table.Column<int>(type: "integer", nullable: false),
                    error_reference_id = table.Column<string>(type: "text", nullable: true),
                    error_content = table.Column<string>(type: "text", nullable: true),
                    artist = table.Column<string>(type: "text", nullable: true),
                    album = table.Column<string>(type: "text", nullable: true),
                    track = table.Column<string>(type: "text", nullable: true),
                    hint_shown = table.Column<bool>(type: "boolean", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_interactions", x => x.id);
                    table.ForeignKey(
                        name: "fk_user_interactions_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_plays",
                columns: table => new
                {
                    user_play_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    track_name = table.Column<string>(type: "citext", nullable: true),
                    album_name = table.Column<string>(type: "citext", nullable: true),
                    artist_name = table.Column<string>(type: "citext", nullable: true),
                    time_played = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ms_played = table.Column<long>(type: "bigint", nullable: true),
                    play_source = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_plays", x => x.user_play_id);
                    table.ForeignKey(
                        name: "fk_user_plays_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_shortcuts",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    input = table.Column<string>(type: "text", nullable: true),
                    output = table.Column<string>(type: "text", nullable: true),
                    created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    modified = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_shortcuts", x => x.id);
                    table.ForeignKey(
                        name: "fk_user_shortcuts_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_streaks",
                columns: table => new
                {
                    user_streak_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    track_name = table.Column<string>(type: "citext", nullable: true),
                    track_playcount = table.Column<int>(type: "integer", nullable: true),
                    album_name = table.Column<string>(type: "citext", nullable: true),
                    album_playcount = table.Column<int>(type: "integer", nullable: true),
                    artist_name = table.Column<string>(type: "citext", nullable: true),
                    artist_playcount = table.Column<int>(type: "integer", nullable: true),
                    streak_started = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    streak_ended = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_streaks", x => x.user_streak_id);
                    table.ForeignKey(
                        name: "fk_user_streaks_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_tracks",
                columns: table => new
                {
                    user_track_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "citext", nullable: true),
                    artist_name = table.Column<string>(type: "citext", nullable: true),
                    playcount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_tracks", x => x.user_track_id);
                    table.ForeignKey(
                        name: "fk_user_tracks_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "album_images",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    album_id = table.Column<int>(type: "integer", nullable: false),
                    image_source = table.Column<int>(type: "integer", nullable: false),
                    image_type = table.Column<int>(type: "integer", nullable: false),
                    url = table.Column<string>(type: "text", nullable: true),
                    last_updated = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    width = table.Column<int>(type: "integer", nullable: true),
                    height = table.Column<int>(type: "integer", nullable: true),
                    bg_color = table.Column<string>(type: "text", nullable: true),
                    text_color1 = table.Column<string>(type: "text", nullable: true),
                    text_color2 = table.Column<string>(type: "text", nullable: true),
                    text_color3 = table.Column<string>(type: "text", nullable: true),
                    text_color4 = table.Column<string>(type: "text", nullable: true),
                    preview_frame_url = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_album_images", x => x.id);
                    table.ForeignKey(
                        name: "fk_album_images_albums_album_id",
                        column: x => x.album_id,
                        principalTable: "albums",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "censored_music_report",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    is_artist = table.Column<bool>(type: "boolean", nullable: false),
                    artist_name = table.Column<string>(type: "text", nullable: true),
                    album_name = table.Column<string>(type: "text", nullable: true),
                    provided_note = table.Column<string>(type: "text", nullable: true),
                    report_status = table.Column<int>(type: "integer", nullable: false),
                    reported_by_discord_user_id = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    processed_by_discord_user_id = table.Column<decimal>(type: "numeric(20,0)", nullable: true),
                    artist_id = table.Column<int>(type: "integer", nullable: true),
                    album_id = table.Column<int>(type: "integer", nullable: true),
                    reported_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    processed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_censored_music_report", x => x.id);
                    table.ForeignKey(
                        name: "fk_censored_music_report_albums_album_id",
                        column: x => x.album_id,
                        principalTable: "albums",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_censored_music_report_artists_artist_id",
                        column: x => x.artist_id,
                        principalTable: "artists",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "tracks",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "citext", nullable: true),
                    artist_id = table.Column<int>(type: "integer", nullable: true),
                    album_id = table.Column<int>(type: "integer", nullable: true),
                    mbid = table.Column<Guid>(type: "uuid", nullable: true),
                    isrc = table.Column<string>(type: "text", nullable: true),
                    spotify_id = table.Column<string>(type: "text", nullable: true),
                    apple_music_id = table.Column<int>(type: "integer", nullable: true),
                    last_fm_url = table.Column<string>(type: "text", nullable: true),
                    last_fm_description = table.Column<string>(type: "text", nullable: true),
                    lastfm_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    artist_name = table.Column<string>(type: "citext", nullable: true),
                    album_name = table.Column<string>(type: "citext", nullable: true),
                    danceability = table.Column<float>(type: "real", nullable: true),
                    energy = table.Column<float>(type: "real", nullable: true),
                    key = table.Column<int>(type: "integer", nullable: true),
                    loudness = table.Column<float>(type: "real", nullable: true),
                    speechiness = table.Column<float>(type: "real", nullable: true),
                    acousticness = table.Column<float>(type: "real", nullable: true),
                    instrumentalness = table.Column<float>(type: "real", nullable: true),
                    liveness = table.Column<float>(type: "real", nullable: true),
                    valence = table.Column<float>(type: "real", nullable: true),
                    tempo = table.Column<float>(type: "real", nullable: true),
                    popularity = table.Column<int>(type: "integer", nullable: true),
                    duration_ms = table.Column<int>(type: "integer", nullable: true),
                    apple_music_url = table.Column<string>(type: "text", nullable: true),
                    apple_music_tagline = table.Column<string>(type: "text", nullable: true),
                    apple_music_description = table.Column<string>(type: "text", nullable: true),
                    apple_music_short_description = table.Column<string>(type: "text", nullable: true),
                    spotify_preview_url = table.Column<string>(type: "text", nullable: true),
                    apple_music_preview_url = table.Column<string>(type: "text", nullable: true),
                    apple_music_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    spotify_last_updated = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    lyrics_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    plain_lyrics = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tracks", x => x.id);
                    table.ForeignKey(
                        name: "fk_tracks_albums_album_id",
                        column: x => x.album_id,
                        principalTable: "albums",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_tracks_artists_artist_id",
                        column: x => x.artist_id,
                        principalTable: "artists",
                        principalColumn: "id");
                });

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
                name: "ix_ai_generations_user_id",
                table: "ai_generations",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_album_images_album_id",
                table: "album_images",
                column: "album_id");

            migrationBuilder.CreateIndex(
                name: "ix_albums_artist_id",
                table: "albums",
                column: "artist_id");

            migrationBuilder.CreateIndex(
                name: "ix_artist_aliases_artist_id",
                table: "artist_aliases",
                column: "artist_id");

            migrationBuilder.CreateIndex(
                name: "ix_artist_genres_artist_id",
                table: "artist_genres",
                column: "artist_id");

            migrationBuilder.CreateIndex(
                name: "ix_artist_images_artist_id",
                table: "artist_images",
                column: "artist_id");

            migrationBuilder.CreateIndex(
                name: "ix_artist_links_artist_id",
                table: "artist_links",
                column: "artist_id");

            migrationBuilder.CreateIndex(
                name: "ix_censored_music_report_album_id",
                table: "censored_music_report",
                column: "album_id");

            migrationBuilder.CreateIndex(
                name: "ix_censored_music_report_artist_id",
                table: "censored_music_report",
                column: "artist_id");

            migrationBuilder.CreateIndex(
                name: "ix_channels_discord_channel_id",
                table: "channels",
                column: "discord_channel_id");

            migrationBuilder.CreateIndex(
                name: "ix_channels_guild_id",
                table: "channels",
                column: "guild_id");

            migrationBuilder.CreateIndex(
                name: "ix_discogs_format_descriptions_release_id",
                table: "discogs_format_descriptions",
                column: "release_id");

            migrationBuilder.CreateIndex(
                name: "ix_discogs_genre_release_id",
                table: "discogs_genre",
                column: "release_id");

            migrationBuilder.CreateIndex(
                name: "ix_discogs_style_release_id",
                table: "discogs_style",
                column: "release_id");

            migrationBuilder.CreateIndex(
                name: "ix_featured_logs_user_id",
                table: "featured_logs",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_friends_friend_user_id",
                table: "friends",
                column: "friend_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_friends_user_id",
                table: "friends",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_guild_blocked_users_user_id",
                table: "guild_blocked_users",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_guild_shortcuts_guild_id",
                table: "guild_shortcuts",
                column: "guild_id");

            migrationBuilder.CreateIndex(
                name: "ix_guild_users_user_id",
                table: "guild_users",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_guilds_discord_guild_id",
                table: "guilds",
                column: "discord_guild_id");

            migrationBuilder.CreateIndex(
                name: "ix_inactive_user_log_user_id",
                table: "inactive_user_log",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_jumble_session_answers_jumble_session_id",
                table: "jumble_session_answers",
                column: "jumble_session_id");

            migrationBuilder.CreateIndex(
                name: "ix_jumble_session_hint_jumble_session_id",
                table: "jumble_session_hint",
                column: "jumble_session_id");

            migrationBuilder.CreateIndex(
                name: "ix_track_synced_lyrics_track_id",
                table: "track_synced_lyrics",
                column: "track_id");

            migrationBuilder.CreateIndex(
                name: "ix_tracks_album_id",
                table: "tracks",
                column: "album_id");

            migrationBuilder.CreateIndex(
                name: "ix_tracks_artist_id",
                table: "tracks",
                column: "artist_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_albums_user_id",
                table: "user_albums",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_artists_user_id",
                table: "user_artists",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_crowns_guild_id",
                table: "user_crowns",
                column: "guild_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_crowns_user_id",
                table: "user_crowns",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_discogs_releases_release_id",
                table: "user_discogs_releases",
                column: "release_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_discogs_releases_user_id",
                table: "user_discogs_releases",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_interactions_user_id",
                table: "user_interactions",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_shortcuts_user_id",
                table: "user_shortcuts",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_streaks_user_id",
                table: "user_streaks",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_tokens_discord_user_id",
                table: "user_tokens",
                column: "discord_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_tracks_user_id",
                table: "user_tracks",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_users_discord_user_id",
                table: "users",
                column: "discord_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_webhooks_guild_id",
                table: "webhooks",
                column: "guild_id");

            // Manual indexes not managed by EF Core

            // Case-insensitive name lookups
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS ix_user_user_name_last_fm ON public.users (UPPER(user_name_last_fm));");
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS ix_artist_artist_name ON public.artists (UPPER(name::text));");
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS ix_albums_name ON public.albums (UPPER(name::text));");
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS ix_tracks_name ON public.tracks (UPPER(name::text));");
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS ix_user_crowns_artist_name_upper ON public.user_crowns (UPPER(artist_name::text));");
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS ix_featured_logs_artist_name_upper ON public.featured_logs (UPPER(artist_name::text));");

            // Jumble session indexes
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS ix_jumble_session_answers_discord_user_id ON public.jumble_session_answers (discord_user_id);");
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS idx_discord_channel_date_started ON public.jumble_sessions (discord_channel_id, date_started DESC);");
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS idx_user_id_date_started ON public.jumble_sessions (starter_user_id, date_started DESC);");

            // Interaction lookup indexes
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS ix_user_interactions_discord_id ON public.user_interactions (discord_id);");
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS ix_user_interactions_discord_response_id ON public.user_interactions (discord_response_id);");

            // Full-text search (GIN indexes)
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS albums_search_fts_gin_idx ON public.albums USING gin (to_tsvector('english', COALESCE(name, '') || ' ' || COALESCE(artist_name, '')));");
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS tracks_search_fts_gin_idx ON public.tracks USING gin (to_tsvector('english', COALESCE(name, '') || ' ' || COALESCE(artist_name, '') || ' ' || COALESCE(album_name, '')));");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ai_generations");

            migrationBuilder.DropTable(
                name: "ai_prompts");

            migrationBuilder.DropTable(
                name: "album_images");

            migrationBuilder.DropTable(
                name: "artist_aliases");

            migrationBuilder.DropTable(
                name: "artist_genres");

            migrationBuilder.DropTable(
                name: "artist_images");

            migrationBuilder.DropTable(
                name: "artist_links");

            migrationBuilder.DropTable(
                name: "botted_user_report");

            migrationBuilder.DropTable(
                name: "botted_users");

            migrationBuilder.DropTable(
                name: "censored_music");

            migrationBuilder.DropTable(
                name: "censored_music_report");

            migrationBuilder.DropTable(
                name: "channels");

            migrationBuilder.DropTable(
                name: "discogs_format_descriptions");

            migrationBuilder.DropTable(
                name: "discogs_genre");

            migrationBuilder.DropTable(
                name: "discogs_style");

            migrationBuilder.DropTable(
                name: "featured_logs");

            migrationBuilder.DropTable(
                name: "friends");

            migrationBuilder.DropTable(
                name: "global_filtered_users");

            migrationBuilder.DropTable(
                name: "guild_blocked_users");

            migrationBuilder.DropTable(
                name: "guild_shortcuts");

            migrationBuilder.DropTable(
                name: "guild_users");

            migrationBuilder.DropTable(
                name: "inactive_user_log");

            migrationBuilder.DropTable(
                name: "jumble_session_answers");

            migrationBuilder.DropTable(
                name: "jumble_session_hint");

            migrationBuilder.DropTable(
                name: "stripe_pricing");

            migrationBuilder.DropTable(
                name: "stripe_supporters");

            migrationBuilder.DropTable(
                name: "supporters");

            migrationBuilder.DropTable(
                name: "track_synced_lyrics");

            migrationBuilder.DropTable(
                name: "user_albums");

            migrationBuilder.DropTable(
                name: "user_artists");

            migrationBuilder.DropTable(
                name: "user_crowns");

            migrationBuilder.DropTable(
                name: "user_discogs");

            migrationBuilder.DropTable(
                name: "user_discogs_releases");

            migrationBuilder.DropTable(
                name: "user_fm_settings");

            migrationBuilder.DropTable(
                name: "user_interactions");

            migrationBuilder.DropTable(
                name: "user_plays");

            migrationBuilder.DropTable(
                name: "user_shortcuts");

            migrationBuilder.DropTable(
                name: "user_streaks");

            migrationBuilder.DropTable(
                name: "user_tokens");

            migrationBuilder.DropTable(
                name: "user_tracks");

            migrationBuilder.DropTable(
                name: "webhooks");

            migrationBuilder.DropTable(
                name: "jumble_sessions");

            migrationBuilder.DropTable(
                name: "tracks");

            migrationBuilder.DropTable(
                name: "discogs_releases");

            migrationBuilder.DropTable(
                name: "users");

            migrationBuilder.DropTable(
                name: "guilds");

            migrationBuilder.DropTable(
                name: "albums");

            migrationBuilder.DropTable(
                name: "artists");
        }
    }
}
