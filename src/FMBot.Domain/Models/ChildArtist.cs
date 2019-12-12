using System;

namespace FMBot.Domain.Models
{
    public class ChildArtist
    {
        public string Name { get; set; }
        public Guid Mbid { get; set; }
        public Uri Url { get; set; }
    }
}
