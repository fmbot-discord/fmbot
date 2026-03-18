using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace FMBot.Bot.Services;

public static class ComponentInteractionTracker
{
    private const int MaxEntriesPerGuild = 200;
    private const string PaginatorPrefix = "component_paginator";

    private static readonly ConcurrentDictionary<ulong, LinkedList<InteractionEntry>> GuildInteractions = new();

    public record InteractionEntry(
        ulong UserId,
        string CustomId,
        ulong ChannelId,
        DateTimeOffset Timestamp);

    public static void Track(ulong? guildId, ulong userId, string customId, ulong channelId)
    {
        if (!guildId.HasValue)
        {
            return;
        }

        var baseId = customId.Split(':')[0];
        if (!baseId.StartsWith(PaginatorPrefix))
        {
            return;
        }

        var list = GuildInteractions.GetOrAdd(guildId.Value, _ => new LinkedList<InteractionEntry>());

        lock (list)
        {
            list.AddFirst(new InteractionEntry(userId, customId, channelId, DateTimeOffset.UtcNow));

            if (list.Count > MaxEntriesPerGuild)
            {
                list.RemoveLast();
            }
        }
    }

    public static List<InteractionEntry> GetRecentForGuild(ulong guildId)
    {
        if (!GuildInteractions.TryGetValue(guildId, out var list))
        {
            return [];
        }

        lock (list)
        {
            return list.ToList();
        }
    }

    public static void RemoveGuild(ulong guildId)
    {
        GuildInteractions.TryRemove(guildId, out _);
    }
}
