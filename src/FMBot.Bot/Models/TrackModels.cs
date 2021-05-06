using FMBot.Domain.Models;

namespace FMBot.Bot.Models
{
    public class ListTrack
    {
        public string ArtistName { get; set; }

        public string TrackName { get; set; }

        public int TotalPlaycount { get; set; }

        public int ListenerCount { get; set; }
    }

    public class WhoKnowsTrackDto
    {
        public int UserId { get; set; }

        public string Name { get; set; }

        public string ArtistName { get; set; }

        public int Playcount { get; set; }

        public string UserNameLastFm { get; set; }

        public ulong DiscordUserId { get; set; }

        public string UserName { get; set; }
    }

    public class WhoKnowsGlobalTrackDto
    {
        public int UserId { get; set; }

        public string Name { get; set; }

        public string ArtistName { get; set; }

        public int Playcount { get; set; }

        public string UserNameLastFm { get; set; }

        public ulong DiscordUserId { get; set; }

        public PrivacyLevel PrivacyLevel { get; set; }
    }

    public class AlbumTrackDto
    {
        public string TrackName { get; set; }

        public string ArtistName { get; set; }

        public int Playcount { get; set; }
    }


    public class TrackSearchResult
    {
        public string TrackName { get; set; }
        public string ArtistName { get; set; }
        public string AlbumName { get; set; }
    }
}
