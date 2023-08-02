using FMBot.Domain.Flags;

namespace FMBot.Persistence.Domain.Models;

public class ArtistAlias
{
    public int Id { get; set; }

    public int ArtistId { get; set; }

    public string Alias { get; set; }

    public bool CorrectsInScrobbles { get; set; }

    public AliasOption Options { get; set; }

    public Artist Artist { get; set; }
}
