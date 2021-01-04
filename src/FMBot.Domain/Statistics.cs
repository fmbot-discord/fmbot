using Prometheus;

namespace FMBot.Domain
{
    public static class Statistics
    {
        public static readonly Gauge DiscordServerCount = Metrics
            .CreateGauge("discord_server_count", "Total count of all servers the bot is in");


        public static readonly Counter LastfmApiCalls = Metrics
            .CreateCounter("lastfm_api_calls", "Amount of last.fm API calls");

        public static readonly Counter LastfmAuthorizedApiCalls = Metrics 
            .CreateCounter("lastfm_authorized_api_calls", "Amount of authorized last.fm API calls");

        public static readonly Counter LastfmImageCalls = Metrics
            .CreateCounter("lastfm_image_cdn_calls", "Amount of calls to the last.fm image cdn");

        public static readonly Counter LastfmCachedImageCalls = Metrics
            .CreateCounter("lastfm_cached_image_cdn_calls", "Amount of calls locally cached to last.fm images");

        public static readonly Histogram LastfmApiResponseTime = Metrics
            .CreateHistogram("lastfm_api_response_time", "Histogram of Last.fm API response time");


        public static readonly Counter CommandsExecuted = Metrics
            .CreateCounter("bot_commands_executed", "Amount of commands executed");

        public static readonly Counter SlashCommandsExecuted = Metrics
            .CreateCounter("bot_slash_commands_executed", "Amount of slash commands executed");
        

        public static readonly Gauge RegisteredUserCount = Metrics
            .CreateGauge("bot_registered_users_count", "Total count of all users in the database");

        public static readonly Gauge AuthorizedUserCount = Metrics
            .CreateGauge("bot_authorized_users_count", "Total count of all users that authorized Last.fm");

        public static readonly Gauge RegisteredGuildCount = Metrics
            .CreateGauge("bot_registered_guilds_count", "Total count of all guilds in the database");
        

        public static readonly Counter IndexedUsers = Metrics
            .CreateCounter("bot_indexed_users", "Amount of indexed users");

        public static readonly Counter UpdatedUsers = Metrics
            .CreateCounter("bot_updated_users", "Amount of updated users");
    }
}
