using System;
using FMBot.Domain.Models;

namespace FMBot.Persistence.Domain.Models;

public class FeaturedLog
{
    public int FeaturedLogId { get; set; }

    public bool HasFeatured { get; set; }
    public bool? NoUpdate { get; set; }

    public int? UserId { get; set; }

    public BotType BotType { get; set; }

    public FeaturedMode FeaturedMode { get; set; }

    public string Description { get; set; }

    public string ImageUrl { get; set; }

    public string TrackName { get; set; }

    public string ArtistName { get; set; }

    public string AlbumName { get; set; }

    public DateTime DateTime { get; set; }

    public bool SupporterDay { get; set; }

    public string FullSizeImage { get; set; }
    
    public string Status { get; set; }
    
    public string[] Reactions { get; set; }

    public User User { get; set; }
}
