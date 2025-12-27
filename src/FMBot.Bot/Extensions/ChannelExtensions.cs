using System.Collections.Generic;
using System;
using System.Linq;
using NetCord;
using NetCord.Gateway;

namespace FMBot.Bot.Extensions;

public static class ChannelExtensions
{
    public static Dictionary<TextGuildChannel, int> GetCategoryChannelPositions(this CategoryGuildChannel category, Guild guild)
    {
        var channelsInCategory = guild.Channels.Values
            .Where(c => c is TextGuildChannel tgc && tgc.ParentId == category.Id)
            .Cast<TextGuildChannel>()
            .OrderBy(ChannelGroup)
            .ThenBy(c => c.Position)
            .ThenBy(c => c.Id)
            .Select((channel, index) => (Channel: channel, Index: index))
            .ToDictionary(g => g.Channel, g => g.Index);

        return channelsInCategory;
    }

    private static int ChannelGroup(IGuildChannel type) => type switch
    {
        VoiceGuildChannel or StageGuildChannel => 2,
        TextGuildChannel or AnnouncementGuildChannel or ForumGuildChannel => 1,
        _ => 0
    };
}
