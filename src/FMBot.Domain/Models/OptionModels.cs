using IF.Lastfm.Core.Api.Enums;

namespace FMBot.Domain.Models
{
    public class TimeSettingsModel
    {
        public string UrlParameter { get; set; }
        public string ApiParameter { get; set; }
        public string Description { get; set; }
        public LastStatsTimeSpan LastStatsTimeSpan { get; set; }
        public ChartTimePeriod ChartTimePeriod { get; set; }
        public bool UsePlays { get; set; }
        public int? PlayDays { get; set; }
    }

    public class UserSettingsModel
    {
        public string UserNameLastFm { get; set; }
        public string SessionKeyLastFm { get; set; }
        public bool DifferentUser { get; set; }
        public ulong DiscordUserId { get; set; }
        public string DiscordUserName { get; set; }
        public int UserId { get; set; }
        public UserType UserType { get; set; }
        public string NewSearchValue { get; set; }
    }
}
