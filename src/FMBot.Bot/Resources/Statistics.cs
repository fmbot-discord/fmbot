using Prometheus;

namespace FMBot.Bot.Resources
{
    public static class Statistics
    {
        public static readonly Gauge DiscordServerCount = Metrics
            .CreateGauge("discord_server_count", "Number of servers the bot is in");

        public static readonly Gauge LastfmApiCalls = Metrics
            .CreateGauge("lastfm_api_calls", "Amount of last.fm API calls");

        public static readonly Gauge LastfmImageCalls = Metrics
            .CreateGauge("lastfm_image_cdn_calls", "Amount of calls to the last.fm image cdn");

        public static readonly Gauge LastfmCachedImageCalls = Metrics
            .CreateGauge("lastfm_cached_image_cdn_calls", "Amount of calls locally cached to last.fm images");

        public static readonly Gauge CommandsExecuted = Metrics
            .CreateGauge("bot_commands_executed", "Amount of commands executed");

        public static readonly Gauge RegisteredUsers = Metrics
            .CreateGauge("bot_registered_users", "Amount of users in the database");

        public static readonly Gauge RegisteredGuilds = Metrics
            .CreateGauge("bot_registered_guilds", "Amount of guilds in the database");
    }
}
