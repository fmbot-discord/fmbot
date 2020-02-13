using Prometheus;

namespace FMBot.Bot.Resources
{
    public static class Statistics
    {
        public static readonly Gauge DiscordServerCount = Metrics
            .CreateGauge("discord_server_count", "Number of servers the bot is in");

        public static readonly Gauge CommandsExecuted = Metrics
            .CreateGauge("commands_executed", "Amount of commands executed");

        public static readonly Gauge LastfmApiCalls = Metrics
            .CreateGauge("lastfm_api_calls", "Amount of last.fm API calls");

        public static readonly Gauge RegisteredUsers = Metrics
            .CreateGauge("registered_users", "Amount of users in the database");

        public static readonly Gauge RegisteredGuilds = Metrics
            .CreateGauge("registered_guilds", "Amount of guilds in the database");
    }
}
