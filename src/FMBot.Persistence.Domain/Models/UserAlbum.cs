using System;

namespace FMBot.Persistence.Domain.Models
{
    public class UserAlbum
    {
        public int UserAlbumId { get; set; }

        public int UserId { get; set; }

        public string Name { get; set; }

        public int Playcount { get; set; }

        public DateTime LastUpdated { get; set; }

        public User User { get; set; }
    }
}
