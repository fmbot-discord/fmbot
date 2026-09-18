using Prometheus;

namespace FMBot.LastFM;

public static class LastFmStatistics
{
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

    public static readonly Histogram LastfmApiResponseTime = Metrics
        .CreateHistogram("lastfm_api_response_time", "Histogram of Last.fm API response time",
            new HistogramConfiguration
            {
                LabelNames = new[] { "method" }
            });

    public static readonly Counter LastfmErrors = Metrics
        .CreateCounter("lastfm_errors", "Amount of errors Last.fm is returning",
            new CounterConfiguration
            {
                LabelNames = new[] { "method" }
            });

    public static readonly Counter LastfmFailureErrors = Metrics
        .CreateCounter("lastfm_errors_failure", "Amount of failure errors Last.fm is returning",
            new CounterConfiguration
            {
                LabelNames = new[] { "method" }
            });

    public static readonly Counter LastfmBadAuthErrors = Metrics
        .CreateCounter("lastfm_errors_badauth", "Amount of badauth errors Last.fm is returning",
            new CounterConfiguration
            {
                LabelNames = new[] { "method" }
            });

    public static readonly Counter SmallIndexedUsers = Metrics
        .CreateCounter("bot_smallindexed_users", "Amount of small indexed users");
}
