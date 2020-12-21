using Prometheus;

namespace FMBot.Domain
{
    public static class Statistics
    {
        public static readonly Gauge DiscordServerCount = Metrics
            .CreateGauge("discord_server_count", "Total count of all servers the bot is in");

        public static readonly Gauge LastfmApiCalls = Metrics
            .CreateGauge("lastfm_api_calls", "Amount of last.fm API calls");

        public static readonly Gauge LastfmAuthorizedApiCalls = Metrics
            .CreateGauge("lastfm_authorized_api_calls", "Amount of authorized last.fm API calls");

        public static readonly Gauge LastfmImageCalls = Metrics
            .CreateGauge("lastfm_image_cdn_calls", "Amount of calls to the last.fm image cdn");

        public static readonly Gauge LastfmCachedImageCalls = Metrics
            .CreateGauge("lastfm_cached_image_cdn_calls", "Amount of calls locally cached to last.fm images");

        public static readonly Gauge CommandsExecuted = Metrics
            .CreateGauge("bot_commands_executed", "Amount of commands executed");
        
        public static readonly Gauge SlashCommandsExecuted = Metrics
            .CreateGauge("bot_slash_commands_executed", "Amount of slash commands executed");
        
        public static readonly Gauge RegisteredUserCount = Metrics
            .CreateGauge("bot_registered_users_count", "Total count of all users in the database");
        
        public static readonly Gauge AuthorizedUserCount = Metrics
            .CreateGauge("bot_authorized_users_count", "Total count of all users that authorized Last.fm");

        public static readonly Gauge RegisteredGuildCount = Metrics
            .CreateGauge("bot_registered_guilds_count", "Total count of all guilds in the database");

        public static readonly Gauge IndexedUsers = Metrics
            .CreateGauge("bot_indexed_users", "Amount of indexed users");

        public static readonly Gauge UpdatedUsers = Metrics
            .CreateGauge("bot_updated_users", "Amount of updated users");
    }
}
