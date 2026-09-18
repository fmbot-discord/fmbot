using Prometheus;

namespace FMBot.Core;

public static class CoreStatistics
{
    public static readonly Counter UpdatedUsers = Metrics
        .CreateCounter("bot_updated_users", "Amount of updated users", new CounterConfiguration
        {
            LabelNames = ["reason"]
        });
}
