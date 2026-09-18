using System.Collections.Generic;
using FMBot.Domain.Models;

namespace FMBot.Bot.Extensions;

public static class WhoKnowsObjectExtensions
{
    public static IList<WhoKnowsObjectWithUser> WithDiscordDisplayNames(this IList<WhoKnowsObjectWithUser> whoKnowsObjects,
        NetCord.Gateway.Guild discordGuild, IDictionary<int, FullGuildUser> guildUsers, int? maxRows = null)
    {
        if (discordGuild == null)
        {
            return whoKnowsObjects;
        }

        for (var i = 0; i < whoKnowsObjects.Count; i++)
        {
            if (maxRows.HasValue && i >= maxRows.Value)
            {
                break;
            }

            var whoKnowsObject = whoKnowsObjects[i];
            if (guildUsers.TryGetValue(whoKnowsObject.UserId, out var guildUser) &&
                discordGuild.Users.TryGetValue(guildUser.DiscordUserId, out var discordGuildUser))
            {
                whoKnowsObject.DiscordName = discordGuildUser.GetDisplayName();
            }
        }

        return whoKnowsObjects;
    }
}
