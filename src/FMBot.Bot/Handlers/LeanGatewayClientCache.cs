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
        return new LeanGatewayClientCache();
    }
}

public sealed class LeanGatewayClientCache : IGatewayClientCache
{
    private CurrentUser _user;
    private readonly ConcurrentDictionary<ulong, Guild> _guilds = new();

    public CurrentUser User => this._user;

    public IReadOnlyDictionary<ulong, Guild> Guilds => this._guilds;

    private static class Empty<TKey, TValue> where TKey : notnull where TValue : class
    {
        public static readonly IReadOnlyDictionary<TKey, TValue> Instance = new Dictionary<TKey, TValue>(0);
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
        if (items.Count == 0)
        {
            return Empty<TKey, TValue>.Instance;
        }

        var dictionary = new ConcurrentDictionary<TKey, TValue>(1, items.Count);
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

    private static ConcurrentDictionary<TKey, TValue> Writable<TKey, TValue>(Guild guild,
        Func<Guild, IReadOnlyDictionary<TKey, TValue>> get, Action<Guild, ConcurrentDictionary<TKey, TValue>> set)
        where TKey : notnull where TValue : class
    {
        if (get(guild) is ConcurrentDictionary<TKey, TValue> existing)
        {
            return existing;
        }

        lock (guild)
        {
            if (get(guild) is ConcurrentDictionary<TKey, TValue> raced)
            {
                return raced;
            }

            var created = new ConcurrentDictionary<TKey, TValue>(1, 4);
            set(guild, created);
            return created;
        }
    }

    private static void Remove<TKey, TValue>(Guild guild, Func<Guild, IReadOnlyDictionary<TKey, TValue>> get, TKey key)
        where TKey : notnull where TValue : class
    {
        if (get(guild) is ConcurrentDictionary<TKey, TValue> dictionary)
        {
            dictionary.TryRemove(key, out _);
        }
    }

    public IGatewayClientCache CacheGuild(Guild guild)
    {
        this._guilds[guild.Id] = guild;
        return this;
    }

    public IGatewayClientCache CacheGuildUser(GuildUser user)
    {
        if (this._guilds.TryGetValue(user.GuildId, out var guild))
        {
            Writable(guild, static g => g.Users, static (g, d) => g.Users = d)[user.Id] = user;
        }

        return this;
    }

    public IGatewayClientCache CacheGuildUsers(ulong guildId, IReadOnlyList<GuildUser> users)
    {
        if (this._guilds.TryGetValue(guildId, out var guild))
        {
            var dictionary = Writable(guild, static g => g.Users, static (g, d) => g.Users = d);
            foreach (var user in users)
            {
                dictionary[user.Id] = user;
            }
        }

        return this;
    }

    public IGatewayClientCache CachePresences(ulong guildId, IReadOnlyList<Presence> presences)
    {
        return this;
    }

    public IGatewayClientCache CacheRole(Role role)
    {
        if (this._guilds.TryGetValue(role.GuildId, out var guild))
        {
            Writable(guild, static g => g.Roles, static (g, d) => g.Roles = d)[role.Id] = role;
        }

        return this;
    }

    public IGatewayClientCache CacheGuildScheduledEvent(GuildScheduledEvent scheduledEvent)
    {
        return this;
    }

    public IGatewayClientCache CacheGuildThread(GuildThread thread)
    {
        if (this._guilds.TryGetValue(thread.GuildId, out var guild))
        {
            Writable(guild, static g => g.ActiveThreads, static (g, d) => g.ActiveThreads = d)[thread.Id] = thread;
        }

        return this;
    }

    public IGatewayClientCache CacheGuildChannel(IGuildChannel channel)
    {
        if (this._guilds.TryGetValue(channel.GuildId, out var guild))
        {
            Writable(guild, static g => g.Channels, static (g, d) => g.Channels = d)[channel.Id] = channel;
        }

        return this;
    }

    public IGatewayClientCache CacheStageInstance(StageInstance stageInstance)
    {
        return this;
    }

    public IGatewayClientCache CacheCurrentUser(CurrentUser user)
    {
        this._user = user;
        return this;
    }

    public IGatewayClientCache CacheVoiceState(VoiceState voiceState)
    {
        if (this._guilds.TryGetValue(voiceState.GuildId, out var guild))
        {
            Writable(guild, static g => g.VoiceStates, static (g, d) => g.VoiceStates = d)[voiceState.UserId] = voiceState;
        }

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

    public IGatewayClientCache SyncGuildActiveThreads(ulong guildId, IReadOnlyDictionary<ulong, GuildThread> threads)
    {
        if (this._guilds.TryGetValue(guildId, out var guild))
        {
            guild.ActiveThreads = threads;
        }

        return this;
    }

    public IGatewayClientCache SyncGuilds(IReadOnlyList<ulong> guildIds)
    {
        foreach (var guildId in this._guilds.Keys.Except(guildIds))
        {
            this._guilds.TryRemove(guildId, out _);
        }

        return this;
    }

    public IGatewayClientCache RemoveGuild(ulong guildId)
    {
        this._guilds.TryRemove(guildId, out _);
        return this;
    }

    public IGatewayClientCache RemoveGuildUser(ulong guildId, ulong userId)
    {
        if (this._guilds.TryGetValue(guildId, out var guild))
        {
            Remove(guild, static g => g.Users, userId);
        }

        return this;
    }

    public IGatewayClientCache RemoveRole(ulong guildId, ulong roleId)
    {
        if (this._guilds.TryGetValue(guildId, out var guild))
        {
            Remove(guild, static g => g.Roles, roleId);
        }

        return this;
    }

    public IGatewayClientCache RemoveGuildScheduledEvent(ulong guildId, ulong scheduledEventId)
    {
        return this;
    }

    public IGatewayClientCache RemoveGuildThread(ulong guildId, ulong threadId)
    {
        if (this._guilds.TryGetValue(guildId, out var guild))
        {
            Remove(guild, static g => g.ActiveThreads, threadId);
        }

        return this;
    }

    public IGatewayClientCache RemoveGuildChannel(ulong guildId, ulong channelId)
    {
        if (this._guilds.TryGetValue(guildId, out var guild))
        {
            Remove(guild, static g => g.Channels, channelId);
        }

        return this;
    }

    public IGatewayClientCache RemoveStageInstance(ulong guildId, ulong stageInstanceId)
    {
        return this;
    }

    public IGatewayClientCache RemoveVoiceState(ulong guildId, ulong userId)
    {
        if (this._guilds.TryGetValue(guildId, out var guild))
        {
            Remove(guild, static g => g.VoiceStates, userId);
        }

        return this;
    }

    public void Dispose()
    {
    }
}
