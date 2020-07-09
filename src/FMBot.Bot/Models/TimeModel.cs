using FMBot.Persistence.Domain.Models;
using IF.Lastfm.Core.Api.Enums;

namespace FMBot.Bot.Models
{
    public class TimeModel
    {
        public string UrlParameter { get; set; }
        public string Description { get; set; }
        public LastStatsTimeSpan LastStatsTimeSpan { get; set; }
        public ChartTimePeriod ChartTimePeriod { get; set; }
    }
}
