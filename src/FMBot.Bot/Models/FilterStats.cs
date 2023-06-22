using System.Collections.Generic;
using System.Linq;
using System.Text;
using FMBot.Bot.Extensions;
using FMBot.Bot.Services;

namespace FMBot.Bot.Models;

public class FilterStats
{
    public int StartCount { get; set; }

    public List<ulong> Roles { get; set; }

    public bool RequesterFiltered { get; set; }

    public int? ActivityThresholdFiltered { get; set; }
    public int? GuildActivityThresholdFiltered { get; set; }
    public int? BlockedFiltered { get; set; }
    public int? AllowedRolesFiltered { get; set; }
    public int? BlockedRolesFiltered { get; set; }
    public int? ManualRoleFilter { get; set; }

    public int EndCount { get; set; }

    public string FullDescription
    {
        get
        {
            var descriptionList = new List<string>();

            if (this.ActivityThresholdFiltered is > 0)
            {
                descriptionList.Add($"{this.ActivityThresholdFiltered.Value} inactive .fmbot");
            }
            if (this.GuildActivityThresholdFiltered is > 0)
            {
                descriptionList.Add($"{this.GuildActivityThresholdFiltered.Value} inactive server");
            }
            if (this.BlockedFiltered is > 0)
            {
                descriptionList.Add($"{this.BlockedFiltered.Value} blocked");
            }
            if (this.AllowedRolesFiltered is > 0)
            {
                descriptionList.Add($"{this.AllowedRolesFiltered.Value} without allowed roles");
            }
            if (this.BlockedRolesFiltered is > 0)
            {
                descriptionList.Add($"{this.BlockedRolesFiltered.Value} with blocked roles");
            }

            var description = new StringBuilder();
            if (descriptionList.Any())
            {
                description.Append($"Filtered: {StringService.StringListToLongString(descriptionList)} users");
            }

            if (this.Roles != null && this.Roles.Any())
            {
                description.Append($"✨ Role filter enabled with {this.Roles.Count} {StringExtensions.GetRolesString(this.Roles.Count)} picked");
            }

            return description.Length > 0 ?
                description.ToString() :
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
