using FMBot.Domain.Flags;

namespace FMBot.Domain.Models;

public class CachedAlias
{
    public int ArtistId { get; set; }

    public string Alias { get; set; }

    public string ArtistName { get; set; }

    public AliasOption Options { get; set; }
}
