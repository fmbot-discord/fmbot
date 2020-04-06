namespace FMBot.Domain.BotModels
{
    public class ArtistWithUser
    {
        public string ArtistName { get; set; }

        public int Playcount { get; set; }

        public string LastFMUsername { get; set; }

        public string DiscordName { get; set; }

        public ulong DiscordUserId { get; set; }

        public int UserId { get; set; }
    }
}
