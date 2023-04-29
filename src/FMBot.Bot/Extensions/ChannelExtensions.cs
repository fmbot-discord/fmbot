using Discord.WebSocket;
using Discord;
using System.Collections.Generic;
using System;
using System.Linq;

namespace FMBot.Bot.Extensions;

public static class ChannelExtensions
{
    public static Dictionary<SocketGuildChannel, int> GetCategoryChannelPositions(this SocketCategoryChannel category)
        => category.Channels
            .OrderBy(ChannelGroup).ThenBy(c => c.Position).ThenBy(c => c.Id)
            .Select((channel, index) => (Channel: channel, Index: index))
            .ToDictionary(g => g.Channel, g => g.Index);

    private static int ChannelGroup(IGuildChannel type) => type switch
    {
        IVoiceChannel or IStageChannel => 2,
        INestedChannel or INewsChannel or IForumChannel => 1,
        _ => throw new ArgumentOutOfRangeException(
            nameof(type), type, "This channel type cannot be sorted.")
    };
}
