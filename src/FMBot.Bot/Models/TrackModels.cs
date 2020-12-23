namespace FMBot.Bot.Models
{
    public class ListTrack
    {
        public string ArtistName { get; set; }

        public string TrackName { get; set; }

        public int Playcount { get; set; }

        public int ListenerCount { get; set; }
    }

    public abstract class WhoKnowsTrackDto
    {
        public int UserId { get; set; }

        public string Name { get; set; }

        public string ArtistName { get; set; }

        public int Playcount { get; set; }

        public string UserNameLastFm { get; set; }

        public ulong DiscordUserId { get; set; }
    }
}
