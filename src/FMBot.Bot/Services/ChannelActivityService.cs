using System;
using System.Collections.Generic;
using FMBot.Bot.Extensions;
using FMBot.Bot.Models;
using FMBot.Domain;
using Microsoft.Extensions.Caching.Memory;
using NetCord;

namespace FMBot.Bot.Services;

public class ChannelActivityService
{
    private const int MaxUsersPerChannel = 15;

    private readonly IMemoryCache _cache;

    public ChannelActivityService(IMemoryCache cache)
    {
        this._cache = cache;
    }

    private static string CacheKeyForChannel(ulong channelId)
    {
        return $"channel-activity-{channelId}";
    }

    public void AddUser(ulong channelId, User user, bool startTracking)
    {
        if (user.IsBot || !PublicProperties.RegisteredUsers.ContainsKey(user.Id))
        {
            return;
        }

        var cacheKey = CacheKeyForChannel(channelId);
        if (!this._cache.TryGetValue(cacheKey, out List<RecentChannelUser> recentUsers))
        {
            if (!startTracking)
            {
                return;
            }

            recentUsers = [];
            this._cache.Set(cacheKey, recentUsers, new MemoryCacheEntryOptions
            {
                SlidingExpiration = TimeSpan.FromHours(1)
            });
        }

        var displayName = user is GuildUser guildUser ? guildUser.GetDisplayName() : user.GetDisplayName();

        lock (recentUsers)
        {
            if (recentUsers.Count > 0 &&
                recentUsers[0].DiscordUserId == user.Id &&
                recentUsers[0].DisplayName == displayName)
            {
                return;
            }

            recentUsers.RemoveAll(r => r.DiscordUserId == user.Id);
            recentUsers.Insert(0, new RecentChannelUser(user.Id, displayName, user.Username));

            if (recentUsers.Count > MaxUsersPerChannel)
            {
                recentUsers.RemoveAt(recentUsers.Count - 1);
            }
        }
    }

    public List<RecentChannelUser> GetRecentUsers(ulong channelId)
    {
        if (!this._cache.TryGetValue(CacheKeyForChannel(channelId), out List<RecentChannelUser> recentUsers))
        {
            return [];
        }

        lock (recentUsers)
        {
            return [..recentUsers];
        }
    }
}
