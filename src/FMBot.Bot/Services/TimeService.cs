using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FMBot.Bot.Extensions;
using FMBot.Core;
using FMBot.Domain.Models;
using FMBot.Persistence.Domain.Models;

namespace FMBot.Bot.Services;

public class TimeService : ListeningTimeService
{
    public TimeService(ITimeEnrichment timeEnrichment) : base(timeEnrichment)
    {
    }

    public async Task<List<WhoKnowsObjectWithUser>> UserPlaysToGuildLeaderboard(NetCord.Gateway.Guild discordGuild, List<UserPlay> userPlays, IDictionary<int, FullGuildUser> guildUsers)
    {
        var whoKnowsAlbumList = new List<WhoKnowsObjectWithUser>();

        var userPlaysPerUser = userPlays
            .GroupBy(g => g.UserId)
            .ToList();

        foreach (var user in userPlaysPerUser)
        {
            var timeListened = await EnrichPlaysWithPlayTime(user.ToList());

            if (guildUsers.TryGetValue(user.Key, out var guildUser))
            {
                var userName = guildUser.UserName ?? guildUser.UserNameLastFM;

                if (discordGuild.Users.TryGetValue(guildUser.DiscordUserId, out var discordUser))
                {
                    userName = discordUser.GetDisplayName();
                }

                whoKnowsAlbumList.Add(new WhoKnowsObjectWithUser
                {
                    DiscordName = userName,
                    Playcount = (int)timeListened.totalPlayTime.TotalMinutes,
                    LastFMUsername = guildUser.UserNameLastFM,
                    UserId = user.Key,
                    LastUsed = guildUser.LastUsed,
                    LastMessage = guildUser.LastMessage,
                    Roles = guildUser.Roles,
                    Name = StringExtensions.GetListeningTimeString(timeListened.totalPlayTime)
                });
            }
        }

        return whoKnowsAlbumList
            .OrderByDescending(o => o.Playcount)
            .ToList();
    }
}
