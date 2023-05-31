using System;
using FMBot.Domain.Models;

namespace FMBot.Bot.Models;

public class WhoKnowsSettings
{
    public bool HidePrivateUsers { get; set; }

    public bool ShowBotters { get; set; }

    public bool AdminView { get; set; }

    public string NewSearchValue { get; set; }

    public WhoKnowsMode WhoKnowsMode { get; set; }

    public bool DisplayRoleFilter { get; set; }
}
