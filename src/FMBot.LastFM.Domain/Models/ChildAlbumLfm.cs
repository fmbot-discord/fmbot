using System;

namespace FMBot.LastFM.Domain.Models;

public class ChildAlbumLfm
{
    public string Artist { get; set; }
    public string Title { get; set; }
    public string Mbid { get; set; }
    public string Url { get; set; }
}
