using System;
using IF.Lastfm.Core.Api.Enums;

namespace FMBot.Domain.Models;

public class TimeSettingsModel
{
    public string UrlParameter { get; set; }
    public string ApiParameter { get; set; }
    public string Description { get; set; }
    public string AltDescription { get; set; }
    public LastStatsTimeSpan LastStatsTimeSpan { get; set; }
    public TimePeriod TimePeriod { get; set; }
    public bool UsePlays { get; set; }
    public bool UseCustomTimePeriod { get; set; }
    public DateTime? StartDateTime { get; set; }
    public DateTime? EndDateTime { get; set; }

    public string BillboardTimeDescription { get; set; }
    public DateTime? BillboardStartDateTime { get; set; }
    public DateTime? BillboardEndDateTime { get; set; }

    public int? PlayDays { get; set; }
    public int? PlayDaysWithBillboard { get; set; }
    public string NewSearchValue { get; set; }
    public bool DefaultPicked { get; set; }

    public long? TimeFrom  { get; set; }
    public long? TimeUntil  { get; set; }
}

public class UserSettingsModel
{
    public string UserNameLastFm { get; set; }
    public string TimeZone { get; set; }
    public string SessionKeyLastFm { get; set; }
    public bool DifferentUser { get; set; }
    public ulong DiscordUserId { get; set; }
    public string DisplayName { get; set; }
    public int UserId { get; set; }
    public UserType UserType { get; set; }
    public DateTime? RegisteredLastFm { get; set; }
    public string NewSearchValue { get; set; }
}
