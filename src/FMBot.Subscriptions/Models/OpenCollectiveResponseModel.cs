namespace FMBot.Subscriptions.Models;



public class OpenCollectiveResponseModel
{
    public Account Account { get; set; }
}

public class Account
{
    public string Name { get; set; }
    public string Slug { get; set; }
    public Members Members { get; set; }
    public string Id { get; set; }
    public DateTime CreatedAt { get; set; }
    public Transactions Transactions { get; set; }
}

public class Amount
{
    public string Currency { get; set; }
    public double Value { get; set; }
}

public class Members
{
    public int TotalCount { get; set; }
    public List<Node> Nodes { get; set; }
}

public class Node
{
    public Account Account { get; set; }
    public DateTime CreatedAt { get; set; }
    public Amount Amount { get; set; }
    public string Description { get; set; }
    public string Type { get; set; }
    public string Kind { get; set; }
}
public class Transactions
{
    public List<Node> Nodes { get; set; }
}
