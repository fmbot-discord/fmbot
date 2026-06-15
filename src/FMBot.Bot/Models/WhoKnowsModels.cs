using System;
using System.Security.AccessControl;
using FMBot.Domain.Models;

namespace FMBot.Bot.Models;

public class GlobalFilterCandidate
{
    public int UserId { get; set; }

    public ulong DiscordUserId { get; set; }

    public string UserNameLastFm { get; set; }

    public int PlayCount { get; set; }

    public long TotalMsPlayed { get; set; }

    public DateTime FirstPlay { get; set; }

    public DateTime LastPlay { get; set; }
}

public class WhoKnowsSettings
{
    public WhoKnowsSettings()
    {
        this.RedirectsEnabled = true;
    }

    public bool HidePrivateUsers { get; set; }

    public bool ShowBotters { get; set; }

    public bool QualityFilterDisabled { get; set; } = false;

    public bool AdminView { get; set; }

    public string NewSearchValue { get; set; }

    public WhoKnowsResponseMode ResponseMode { get; set; }

    public bool DisplayRoleFilter { get; set; }

    public bool RedirectsEnabled { get; set; }
}
