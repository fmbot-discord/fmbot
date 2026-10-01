using Prometheus;

namespace FMBot.Core;

public static class CoreStatistics
{
    public static readonly Counter UpdatedUsers = Metrics
        .CreateCounter("bot_updated_users", "Amount of updated users", new CounterConfiguration
        {
            LabelNames = ["reason"]
        });

    public static readonly Counter DeezerApiCalls = Metrics
        .CreateCounter("deezer_api_calls", "Amount of Deezer API calls");

    public static readonly Counter LastfmImageCalls = Metrics
        .CreateCounter("lastfm_image_cdn_calls", "Amount of calls to the last.fm image cdn");

    public static readonly Counter LastfmCachedImageCalls = Metrics
        .CreateCounter("lastfm_cached_image_cdn_calls", "Amount of calls locally cached to last.fm images");
}
