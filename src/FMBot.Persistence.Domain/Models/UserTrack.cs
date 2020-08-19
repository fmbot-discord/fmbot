using System;

namespace FMBot.Persistence.Domain.Models
{
    public class UserTrack
    {
        public int UserTrackId { get; set; }

        public int UserId { get; set; }

        public string Name { get; set; }

        public string ArtistName { get; set; }

        public int Playcount { get; set; }

        public DateTime LastUpdated { get; set; }

        public User User { get; set; }
    }
}
