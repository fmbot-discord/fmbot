using System;

namespace FMBot.Bot.Models;

public class SearchQueryModel
{
    public string Query { get; set; }
}

public class SearchResultRow
{
    public string Primary { get; set; }
    public string Secondary { get; set; }
    public string Album { get; set; }
    public long Count { get; set; }
    public DateTime? TimePlayed { get; set; }
    public int PlaySource { get; set; }
}
