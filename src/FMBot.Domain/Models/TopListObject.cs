namespace FMBot.Domain.Models;

public class TopListObject
{
    public int UserId { get; set; }

    public string Name { get; set; }

    public int Playcount { get; set; }

    public string LastFMUsername { get; set; }

    public string DiscordName { get; set; }
}
