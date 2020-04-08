using System;

namespace FMBot.LastFM.Domain.Models
{
    public class ChildAlbum
    {
        public string Artist { get; set; }
        public string Title { get; set; }
        public Guid Mbid { get; set; }
        public Uri Url { get; set; }
    }
}
