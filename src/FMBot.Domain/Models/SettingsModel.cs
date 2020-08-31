using IF.Lastfm.Core.Api.Enums;

namespace FMBot.Domain.Models
{
    public class SettingsModel
    {
        public string UrlParameter { get; set; }
        public string ApiParameter { get; set; }
        public string Description { get; set; }
        public LastStatsTimeSpan LastStatsTimeSpan { get; set; }
        public ChartTimePeriod ChartTimePeriod { get; set; }
        public int Amount { get; set; }
        public ulong? OtherDiscordUserId { get; set; }
    }
}
