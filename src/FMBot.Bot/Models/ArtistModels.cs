using FMBot.Domain.Models;

namespace FMBot.Bot.Models
{
    public class ServerArtistSettings
    {
        public OrderType OrderType { get; set; }

        public ChartTimePeriod ChartTimePeriod { get; set; }
    }

    public enum OrderType
    {
        Playcount = 1,
        Listeners = 2
    }
}
