using System.Collections.Generic;

namespace FMBot.Persistence.Domain.Models;

public class Tag
{
    public int Id { get; set; }

    public string Name { get; set; }

    public bool Banned { get; set; }

    public ICollection<ArtistTag> ArtistTags { get; set; }
    public ICollection<AlbumTag> AlbumTags { get; set; }
    public ICollection<TrackTag> TrackTags { get; set; }
}
