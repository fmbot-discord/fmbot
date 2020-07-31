using System;

namespace FMBot.Persistence.Domain.Models
{
    public class Track
    {
        public int Id { get; set; }

        public string Name { get; set; }

        public int? ArtistId { get; set; }

        public string ArtistName { get; set; }

        public string AlbumName { get; set; }

        public string SpotifyId { get; set; }

        public int? Key { get; set; }

        public float? Tempo { get; set; }

        public int? DurationMs { get; set; }

        public DateTime? SpotifyLastUpdated { get; set; }

        public Artist Artist { get; set; }
    }
}
