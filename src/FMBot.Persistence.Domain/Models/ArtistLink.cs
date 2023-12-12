using FMBot.Domain.Enums;

namespace FMBot.Persistence.Domain.Models;

public class ArtistLink
{
    public int Id { get; set; }

    public int ArtistId { get; set; }

    public string Url { get; set; }

    public string Username { get; set; }

    public LinkType Type { get; set; }

    public bool ManuallyAdded { get; set; }

    public Artist Artist { get; set; }
}
