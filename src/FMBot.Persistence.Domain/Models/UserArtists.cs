namespace FMBot.Persistence.Domain.Models
{
    public class UserArtist
    {
        public int UserArtistId { get; set; }

        public int UserId { get; set; }

        public string Name { get; set; }

        public int Playcount { get; set; }

        public User User { get; set; }
    }
}
