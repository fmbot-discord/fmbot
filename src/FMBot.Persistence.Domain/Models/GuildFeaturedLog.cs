using System;
using FMBot.Domain.Models;

namespace FMBot.Persistence.Domain.Models;

public class GuildFeaturedLog
{
    public int Id { get; set; }

    public int GuildId { get; set; }

    public DateTime DateTime { get; set; }

    public FeaturedMode FeaturedMode { get; set; }

    public int? UserId { get; set; }

    public string Description { get; set; }

    public string ImageUrl { get; set; }

    public string ArtistName { get; set; }

    public string AlbumName { get; set; }

    public string TrackName { get; set; }

    public bool HasFeatured { get; set; }

    public Guild Guild { get; set; }
}
