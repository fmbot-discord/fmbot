using System.Collections.Generic;
using System.Linq;
using FMBot.Bot.Extensions;
using FMBot.Bot.Services;

namespace FMBot.Bot.Models;

public class FilterStats
{
    public int StartCount { get; set; }

    public bool RequesterFiltered { get; set; }

    public int? ActivityThresholdFiltered { get; set; }
    public int? GuildActivityThresholdFiltered { get; set; }
    public int? BlockedFiltered { get; set; }
    public int? AllowedRolesFiltered { get; set; }
    public int? BlockedRolesFiltered { get; set; }

    public int EndCount { get; set; }

    public string FullDescription
    {
        get
        {
            var descriptionList = new List<string>();

            if (this.ActivityThresholdFiltered.HasValue)
            {
                descriptionList.Add($"{this.ActivityThresholdFiltered.Value} inactive in .fmbot");
            }
            if (this.GuildActivityThresholdFiltered.HasValue)
            {
                descriptionList.Add($"{this.GuildActivityThresholdFiltered.Value} inactive in server");
            }
            if (this.BlockedFiltered.HasValue)
            {
                descriptionList.Add($"{this.BlockedFiltered.Value} blocked");
            }
            if (this.AllowedRolesFiltered.HasValue)
            {
                descriptionList.Add($"{this.AllowedRolesFiltered.Value} without allowed roles");
            }
            if (this.BlockedRolesFiltered.HasValue)
            {
                descriptionList.Add($"{this.BlockedRolesFiltered.Value} with blocked roles");
            }

            return descriptionList.Any() ?
                $"Filtered: {StringService.StringListToLongString(descriptionList)} users" :
                null;
        }
    }

    public string BasicDescription
    {
        get
        {
            return this.EndCount < this.StartCount ?
                    $"{this.StartCount - this.EndCount} {StringExtensions.GetUsersString(this.StartCount - this.EndCount)} filtered" :
                    null;
        }
    }
}
