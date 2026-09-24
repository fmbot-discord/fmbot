using Prometheus;

namespace FMBot.Discogs;

public static class DiscogsStatistics
{
    public static readonly Counter DiscogsApiCalls = Metrics
        .CreateCounter("discogs_api_calls", "Amount of Discogs API calls");
}
