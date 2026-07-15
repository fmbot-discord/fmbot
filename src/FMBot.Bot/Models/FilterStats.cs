using System.Collections.Generic;
using System.Linq;
using System.Text;
using FMBot.Bot.Services;

namespace FMBot.Bot.Models;

public class FilterStats
{
    public int StartCount { get; set; }

    public List<ulong> Roles { get; set; }

    public bool? RequesterFiltered { get; set; }

    public int? ActivityThresholdFiltered { get; set; }
    public int? GuildActivityThresholdFiltered { get; set; }
    public int? BlockedFiltered { get; set; }
    public int? AllowedRolesFiltered { get; set; }
    public int? BlockedRolesFiltered { get; set; }
    public int? ManualRoleFilter { get; set; }

    public int EndCount { get; set; }

    public string GetFullDescription(Localizer localizer)
    {
        var descriptionList = new List<string>();

        if (this.ActivityThresholdFiltered is > 0)
        {
            descriptionList.Add(localizer.TranslateCount("whoknows.filteredInactiveFmbot", this.ActivityThresholdFiltered.Value));
        }
        if (this.GuildActivityThresholdFiltered is > 0)
        {
            descriptionList.Add(localizer.TranslateCount("whoknows.filteredInactiveServer", this.GuildActivityThresholdFiltered.Value));
        }
        if (this.BlockedFiltered is > 0)
        {
            descriptionList.Add(localizer.TranslateCount("whoknows.filteredBlocked", this.BlockedFiltered.Value));
        }
        if (this.AllowedRolesFiltered is > 0)
        {
            descriptionList.Add(localizer.TranslateCount("whoknows.filteredWithoutAllowedRoles", this.AllowedRolesFiltered.Value));
        }
        if (this.BlockedRolesFiltered is > 0)
        {
            descriptionList.Add(localizer.TranslateCount("whoknows.filteredWithBlockedRoles", this.BlockedRolesFiltered.Value));
        }

        var description = new StringBuilder();
        if (descriptionList.Any())
        {
            description.Append(localizer.Translate("whoknows.filteredTotal",
                ("filters", StringService.StringListToLongString(descriptionList))));
        }

        if (RequesterFiltered == true)
        {
            description.Append($" {localizer.Translate("whoknows.filteredRequester")}");
        }

        if (this.Roles != null && this.Roles.Any())
        {
            if (description.Length > 0)
            {
                description.Append(' ');
            }

            description.Append(localizer.TranslateCount("whoknows.roleFilterEnabled", this.Roles.Count));
        }

        return description.Length > 0 ?
            description.ToString() :
            null;
    }
}
