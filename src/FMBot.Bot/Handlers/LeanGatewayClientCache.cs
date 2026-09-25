using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using NetCord;
using NetCord.Gateway;
using NetCord.JsonModels;
using NetCord.Rest;

namespace FMBot.Bot.Handlers;

public sealed class LeanGatewayClientCacheProvider : IGatewayClientCacheProvider
{
    public static readonly LeanGatewayClientCacheProvider Instance = new();

    private LeanGatewayClientCacheProvider()
    {
    }

    public IGatewayClientCache Create(ulong clientId, RestClient client)
    {
        return new LeanGatewayClientCache(ConcurrentGatewayClientCacheProvider.Empty.Create(clientId, client));
    }
}

public sealed class LeanGatewayClientCache(ConcurrentGatewayClientCache inner) : IGatewayClientCache
{
    public CurrentUser User => inner.User;

    public IReadOnlyDictionary<ulong, Guild> Guilds => inner.Guilds;

    private static class Empty<TKey, TValue> where TKey : notnull where TValue : class
    {
        public static readonly IReadOnlyDictionary<TKey, TValue> Instance = new ConcurrentDictionary<TKey, TValue>(1, 0);
    }

    public IReadOnlyDictionary<TKey, TValue> CreateDictionary<TSource, TKey, TValue>(IEnumerable<TSource> source,
        Func<TSource, TKey> keySelector, Func<TSource, TValue> elementSelector)
        where TKey : notnull where TValue : class
    {
        if (IsDropped<TValue>())
        {
            if (source is Array array)
            {
                Array.Clear(array);
            }

            return Empty<TKey, TValue>.Instance;
        }

        var items = source as IReadOnlyCollection<TSource> ?? source.ToList();
        var dictionary = new ConcurrentDictionary<TKey, TValue>(1, Math.Max(items.Count, 1));
        foreach (var item in items)
        {
            if (item is JsonChannel channel)
            {
                channel.Topic = null;
            }

            dictionary[keySelector(item)] = elementSelector(item);
        }

        return dictionary;
    }

    private static bool IsDropped<TValue>()
    {
        return typeof(TValue) == typeof(GuildEmoji) ||
               typeof(TValue) == typeof(GuildSticker) ||
               typeof(TValue) == typeof(GuildScheduledEvent) ||
               typeof(TValue) == typeof(StageInstance) ||
               typeof(TValue) == typeof(Presence);
    }

    public IGatewayClientCache CacheGuild(Guild guild)
    {
        inner.CacheGuild(guild);
        return this;
    }

    public IGatewayClientCache CacheGuildUser(GuildUser user)
    {
        inner.CacheGuildUser(user);
        return this;
    }

    public IGatewayClientCache CacheGuildUsers(ulong guildId, IReadOnlyList<GuildUser> users)
    {
        inner.CacheGuildUsers(guildId, users);
        return this;
    }

    public IGatewayClientCache CachePresences(ulong guildId, IReadOnlyList<Presence> presences)
    {
        return this;
    }

    public IGatewayClientCache CacheRole(Role role)
    {
        inner.CacheRole(role);
        return this;
    }

    public IGatewayClientCache CacheGuildScheduledEvent(GuildScheduledEvent scheduledEvent)
    {
        return this;
    }

    public IGatewayClientCache CacheGuildThread(GuildThread thread)
    {
        inner.CacheGuildThread(thread);
        return this;
    }

    public IGatewayClientCache CacheGuildChannel(IGuildChannel channel)
    {
        inner.CacheGuildChannel(channel);
        return this;
    }

    public IGatewayClientCache CacheStageInstance(StageInstance stageInstance)
    {
        return this;
    }

    public IGatewayClientCache CacheCurrentUser(CurrentUser user)
    {
        inner.CacheCurrentUser(user);
        return this;
    }

    public IGatewayClientCache CacheVoiceState(VoiceState voiceState)
    {
        inner.CacheVoiceState(voiceState);
        return this;
    }

    public IGatewayClientCache CachePresence(Presence presence)
    {
        return this;
    }

    public IGatewayClientCache SyncGuildEmojis(ulong guildId, IReadOnlyDictionary<ulong, GuildEmoji> emojis)
    {
        return this;
    }

    public IGatewayClientCache SyncGuildStickers(ulong guildId, IReadOnlyDictionary<ulong, GuildSticker> stickers)
    {
        return this;
    }

    public IGatewayClientCache SyncGuildActiveThreads(ulong guildId, IReadOnlyList<ulong> channelIds,
        IReadOnlyDictionary<ulong, GuildThread> threads)
    {
        inner.SyncGuildActiveThreads(guildId, channelIds, threads);
        return this;
    }

    public IGatewayClientCache SyncGuilds(IReadOnlyList<ulong> guildIds)
    {
        inner.SyncGuilds(guildIds);
        return this;
    }

    public IGatewayClientCache RemoveGuild(ulong guildId)
    {
        inner.RemoveGuild(guildId);
        return this;
    }

    public IGatewayClientCache RemoveGuildUser(ulong guildId, ulong userId)
    {
        inner.RemoveGuildUser(guildId, userId);
        return this;
    }

    public IGatewayClientCache RemoveRole(ulong guildId, ulong roleId)
    {
        inner.RemoveRole(guildId, roleId);
        return this;
    }

    public IGatewayClientCache RemoveGuildScheduledEvent(ulong guildId, ulong scheduledEventId)
    {
        return this;
    }

    public IGatewayClientCache RemoveGuildThread(ulong guildId, ulong threadId)
    {
        inner.RemoveGuildThread(guildId, threadId);
        return this;
    }

    public IGatewayClientCache RemoveGuildChannel(ulong guildId, ulong channelId)
    {
        inner.RemoveGuildChannel(guildId, channelId);
        return this;
    }

    public IGatewayClientCache RemoveStageInstance(ulong guildId, ulong stageInstanceId)
    {
        return this;
    }

    public IGatewayClientCache RemoveVoiceState(ulong guildId, ulong userId)
    {
        inner.RemoveVoiceState(guildId, userId);
        return this;
    }

    public void Dispose()
    {
        inner.Dispose();
    }
}
