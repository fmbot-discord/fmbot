using Prometheus;

namespace FMBot.Domain;

public static class Statistics
{
    public static readonly Gauge TotalDiscordServerCount = Metrics
        .CreateGauge("discord_server_count", "Total count of all servers the bot is in");

    public static readonly Gauge ConnectedDiscordServerCount = Metrics
        .CreateGauge("discord_connected_server_count", "Total count of all servers the bot is connected to");


    public static readonly Counter LastfmApiCalls = Metrics
        .CreateCounter("lastfm_api_calls", "Amount of last.fm API calls",
            new CounterConfiguration
            {
                LabelNames = new[] { "method" }
            });

    public static readonly Counter LastfmAuthorizedApiCalls = Metrics 
        .CreateCounter("lastfm_authorized_api_calls", "Amount of authorized last.fm API calls",
            new CounterConfiguration
            {
                LabelNames = new[] { "method" }
            });

    public static readonly Counter LastfmImageCalls = Metrics
        .CreateCounter("lastfm_image_cdn_calls", "Amount of calls to the last.fm image cdn");

    public static readonly Counter LastfmCachedImageCalls = Metrics
        .CreateCounter("lastfm_cached_image_cdn_calls", "Amount of calls locally cached to last.fm images");

    public static readonly Histogram LastfmApiResponseTime = Metrics
        .CreateHistogram("lastfm_api_response_time", "Histogram of Last.fm API response time");


    public static readonly Counter LastfmNowPlayingUpdates = Metrics
        .CreateCounter("lastfm_scrobbling_nowplaying", "Amount of now playing updates sent to Last.fm",
            new CounterConfiguration
            {
                LabelNames = new[] { "bot" }
            });

    public static readonly Counter LastfmScrobbles = Metrics
        .CreateCounter("lastfm_scrobbling_scrobbled", "Amount of scrobbles sent to Last.fm",
            new CounterConfiguration
            {
                LabelNames = new[] { "bot" }
            });

    public static readonly Counter LastfmErrors = Metrics
        .CreateCounter("lastfm_errors", "Amount of errors Last.fm is returning");

    public static readonly Counter LastfmFailureErrors = Metrics
        .CreateCounter("lastfm_errors_failure", "Amount of failure errors Last.fm is returning");

    public static readonly Counter LastfmBadAuthErrors = Metrics
        .CreateCounter("lastfm_errors_badauth", "Amount of badauth errors Last.fm is returning");


    public static readonly Counter SpotifyApiCalls = Metrics
        .CreateCounter("spotify_api_calls", "Amount of Spotify API calls");

    public static readonly Counter MusicBrainzApiCalls = Metrics
        .CreateCounter("musicbrainz_api_calls", "Amount of MusicBrainz API calls");

    public static readonly Counter DiscogsApiCalls = Metrics
        .CreateCounter("discogs_api_calls", "Amount of Discogs API calls");

    public static readonly Counter OpenCollectiveApiCalls = Metrics
        .CreateCounter("opencollective_api_calls", "Amount of OpenCollective API calls");

    public static readonly Counter OpenAiCalls = Metrics
        .CreateCounter("openai_api_calls", "Amount of OpenAI API calls");


    public static readonly Counter CommandsExecuted = Metrics
        .CreateCounter("bot_commands_executed", "Amount of commands executed",
            new CounterConfiguration
            {
                LabelNames = new[] { "name" }
            });

    public static readonly Counter SlashCommandsExecuted = Metrics
        .CreateCounter("bot_slash_commands_executed", "Amount of slash commands executed",
            new CounterConfiguration
            {
                LabelNames = new[] { "name" }
            });


    public static readonly Histogram TextCommandHandlerDuration = Metrics
        .CreateHistogram("bot_text_command_handler_duration", "Histogram of text command handler duration");

    public static readonly Histogram SlashCommandHandlerDuration = Metrics
        .CreateHistogram("bot_slash_command_handler_duration", "Histogram of text command handler duration");


    public static readonly Counter UserCommandsExecuted = Metrics
        .CreateCounter("bot_user_commands_executed", "Amount of user commands executed");

    public static readonly Counter AutoCompletesExecuted = Metrics
        .CreateCounter("bot_autocompletes_executed", "Amount of autocompletes executed");

    public static readonly Counter SelectMenusExecuted = Metrics
        .CreateCounter("bot_select_menus_executed", "Amount of selectmenus executed");

    public static readonly Counter ModalsExecuted = Metrics
        .CreateCounter("bot_modals_executed", "Amount of modals executed");

    public static readonly Counter ButtonExecuted = Metrics
        .CreateCounter("bot_button_executed", "Amount of buttons executed");


    public static readonly Counter DiscordEvents = Metrics
        .CreateCounter("bot_discord_events", "Amount of events through the Discord gateway",
            new CounterConfiguration
            {
                LabelNames = new[] { "name" }
            });


    public static readonly Gauge RegisteredUserCount = Metrics
        .CreateGauge("bot_registered_users_count", "Total count of all users in the database");

    public static readonly Gauge AuthorizedUserCount = Metrics
        .CreateGauge("bot_authorized_users_count", "Total count of all users that authorized Last.fm");

    public static readonly Gauge RegisteredGuildCount = Metrics
        .CreateGauge("bot_registered_guilds_count", "Total count of all guilds in the database");

    public static readonly Gauge ActiveSupporterCount = Metrics
        .CreateGauge("bot_active_supporter_count", "Total count of all guilds in the database");


    public static readonly Gauge OneDayActiveUserCount = Metrics
        .CreateGauge("bot_active_users_count_1d", "Total count of users who've used the bot in the last day");

    public static readonly Gauge SevenDayActiveUserCount = Metrics
        .CreateGauge("bot_active_users_count_7d", "Total count of users who've used the bot in the last 7 days");

    public static readonly Gauge ThirtyDayActiveUserCount = Metrics
        .CreateGauge("bot_active_users_count_30d", "Total count of users who've used the bot in the last 30 days");


    public static readonly Counter IndexedUsers = Metrics
        .CreateCounter("bot_indexed_users", "Amount of indexed users");

    public static readonly Counter UpdatedUsers = Metrics
        .CreateCounter("bot_updated_users", "Amount of updated users");

    public static readonly Counter SmallIndexedUsers = Metrics
        .CreateCounter("bot_smallindexed_users", "Amount of small indexed users");


    public static readonly Counter ShardConnected = Metrics
        .CreateCounter("bot_shard_connected", "A shard has connected");

    public static readonly Counter ShardDisConnected = Metrics
        .CreateCounter("bot_shard_disconnected", "A shard has disconnected");

    public static readonly Gauge ConnectedShards = Metrics
        .CreateGauge("bot_connected_shards", "Gauge of amount of shards that are connected");

}
