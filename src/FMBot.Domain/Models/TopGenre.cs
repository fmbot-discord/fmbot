using System.Collections.Generic;

namespace FMBot.Domain.Models;

public class TopGenre
{
    public string GenreName { get; set; }

    public long? UserPlaycount { get; set; }

    public List<TopArtist> Artists { get; set; }
}
