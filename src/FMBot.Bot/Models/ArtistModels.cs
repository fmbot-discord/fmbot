using FMBot.Domain.Models;

namespace FMBot.Bot.Models
{
    public class GuildRankingSettings
    {
        public OrderType OrderType { get; set; }

        public ChartTimePeriod ChartTimePeriod { get; set; }
    }

    public enum OrderType
    {
        Playcount = 1,
        Listeners = 2
    }

    public class ListArtist
    {
        public string ArtistName { get; set; }

        public int Playcount { get; set; }

        public int ListenerCount { get; set; }
    }

    public abstract class WhoKnowsArtistDto
    {
        public int UserId { get; set; }

        public string Name { get; set; }

        public string ArtistName { get; set; }

        public int Playcount { get; set; }

        public string UserNameLastFm { get; set; }

        public ulong DiscordUserId { get; set; }
    }

    public class AffinityArtist
    {
        public int UserId { get; set; }

        public string ArtistName { get; set; }

        public long Playcount { get; set; }

        public decimal Weight { get; set; }
    }

    public class AffinityArtistResultWithUser
    {
        public string Name { get; set; }

        public decimal MatchPercentage { get; set; }

        public string LastFMUsername { get; set; }

        public ulong DiscordUserId { get; set; }

        public int UserId { get; set; }
    }
}
