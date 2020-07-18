using System;

namespace FMBot.Persistence.Domain.Models
{
    public class Artist
    {
        public int Id { get; set; }

        public string Name { get; set; }

        public string LastFmUrl { get; set; }

        public Guid? Mbid { get; set; }

        public string SpotifyImageUrl { get; set; }

        public DateTime? SpotifyImageDate { get; set; }

        public string SpotifyId { get; set; }

        public string[] Aliases { get; set; }
    }
}
