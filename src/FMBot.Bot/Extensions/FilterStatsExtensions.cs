using System.Collections.Generic;
using System.Linq;
using System.Text;
using FMBot.Bot.Models;
using FMBot.Bot.Services;
using FMBot.Domain.Models;

namespace FMBot.Bot.Extensions;

public static class FilterStatsExtensions
{
    public static string GetFullDescription(this FilterStats filterStats, Localizer localizer)
    {
        var descriptionList = new List<string>();

        if (filterStats.ActivityThresholdFiltered is > 0)
        {
            descriptionList.Add(localizer.TranslateCount("whoknows.filteredInactiveFmbot", filterStats.ActivityThresholdFiltered.Value));
        }
        if (filterStats.GuildActivityThresholdFiltered is > 0)
        {
            descriptionList.Add(localizer.TranslateCount("whoknows.filteredInactiveServer", filterStats.GuildActivityThresholdFiltered.Value));
        }
        if (filterStats.BlockedFiltered is > 0)
        {
            descriptionList.Add(localizer.TranslateCount("whoknows.filteredBlocked", filterStats.BlockedFiltered.Value));
        }
        if (filterStats.AllowedRolesFiltered is > 0)
        {
            descriptionList.Add(localizer.TranslateCount("whoknows.filteredWithoutAllowedRoles", filterStats.AllowedRolesFiltered.Value));
        }
        if (filterStats.BlockedRolesFiltered is > 0)
        {
            descriptionList.Add(localizer.TranslateCount("whoknows.filteredWithBlockedRoles", filterStats.BlockedRolesFiltered.Value));
        }

        var description = new StringBuilder();
        if (descriptionList.Any())
        {
            description.Append(localizer.Translate("whoknows.filteredTotal",
                ("filters", StringService.StringListToLongString(descriptionList))));
        }

        if (filterStats.RequesterFiltered == true)
        {
            description.Append($" {localizer.Translate("whoknows.filteredRequester")}");
        }

        if (filterStats.Roles != null && filterStats.Roles.Any())
        {
            if (description.Length > 0)
            {
                description.Append(' ');
            }

            description.Append(localizer.TranslateCount("shared.roleFilterEnabled", filterStats.Roles.Count));
        }

        return description.Length > 0 ?
            description.ToString() :
            null;
    }
}
