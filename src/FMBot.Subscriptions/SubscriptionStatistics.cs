using Prometheus;

namespace FMBot.Subscriptions;

public static class SubscriptionStatistics
{
    public static readonly Counter OpenCollectiveApiCalls = Metrics
        .CreateCounter("opencollective_api_calls", "Amount of OpenCollective API calls");
}
