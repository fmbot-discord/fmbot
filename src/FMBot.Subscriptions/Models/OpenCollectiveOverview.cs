
using FMBot.Domain.Models;

namespace FMBot.Subscriptions.Models;

public class OpenCollectiveOverview
{
    public List<OpenCollectiveUser> Users { get; set; }
}

public class OpenCollectiveUser
{
    public string Name { get; set; }
    public string Slug { get; set; }
    public string Id { get; set; }
    public DateTime CreatedAt { get; set; }
    public SubscriptionType SubscriptionType { get; set; }
    public DateTime FirstPayment { get; set; }
    public DateTime LastPayment { get; set; }
    public List<OpenCollectiveTransaction> Transactions { get; set; }
}

public class OpenCollectiveTransaction
{
    public DateTime CreatedAt { get; set; }
    public double Amount { get; set; }
    public string Description { get; set; }
    public string Type { get; set; }
    public string Kind { get; set; }
}
