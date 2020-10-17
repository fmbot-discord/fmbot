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
}
