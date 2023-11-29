using System;
using System.Security.AccessControl;
using FMBot.Domain.Models;

namespace FMBot.Bot.Models;

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

    public ResponseMode ResponseMode { get; set; }

    public bool DisplayRoleFilter { get; set; }

    public bool RedirectsEnabled { get; set; }
}
