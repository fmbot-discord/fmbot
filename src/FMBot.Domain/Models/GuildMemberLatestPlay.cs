using System;

namespace FMBot.Domain.Models;

public class GuildMemberLatestPlay
{
    public int UserId { get; set; }
    public string TrackName { get; set; }
    public string ArtistName { get; set; }
    public string AlbumName { get; set; }
    public DateTime TimePlayed { get; set; }
    public string CoverUrl { get; set; }
}
