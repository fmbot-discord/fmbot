namespace FMBot.Domain.Models;

public class StoredPlayResponse
{
    public bool Accepted { get; set; }

    public bool Ignored { get; set; }

    public string IgnoreMessage { get; set; }
}
