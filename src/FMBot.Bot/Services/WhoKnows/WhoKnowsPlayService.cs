using System.Linq;
using System.Collections.Generic;
using FMBot.Bot.Extensions;
using FMBot.Bot.Services.Guild;
using FMBot.Domain.Models;
using FMBot.Persistence.Domain.Models;
using Microsoft.Extensions.Caching.Memory;

namespace FMBot.Bot.Services.WhoKnows;

public class WhoKnowsPlayService
{
    private readonly IMemoryCache _cache;

    public WhoKnowsPlayService(IMemoryCache cache)
    {
        this._cache = cache;
    }

    public string GuildAlsoPlayingTrack(
        int currentUserId,
        IDictionary<int, FullGuildUser> guildUsers,
        Persistence.Domain.Models.Guild guild,
        string artistName,
        string trackName)
    {
        if (guild == null || guildUsers == null || !guildUsers.Any())
        {
            return null;
        }

        var foundUsers = new List<FullGuildUser>();
        var userPlays = new List<UserPlay>();

        var filter = GuildService.FilterGuildUsers(guildUsers, guild);

        foreach (var user in filter.FilteredGuildUsers.Where(w => w.Key != currentUserId))
        {
            var userFound = this._cache.TryGetValue($"{user.Key}-lastplay-track-{artistName.ToLower()}-{trackName.ToLower()}", out UserPlay userPlay);

            if (userFound)
            {
                foundUsers.Add(user.Value);
                userPlays.Add(userPlay);
            }
        }

        if (!foundUsers.Any())
        {
            return null;
        }

        return Description(foundUsers, userPlays, "track");
    }

    public string GuildAlsoPlayingAlbum(
        int currentUserId,
        IDictionary<int, FullGuildUser> guildUsers,
        Persistence.Domain.Models.Guild guild,
        string artistName,
        string albumName)
    {
        if (guild == null || guildUsers == null || !guildUsers.Any())
        {
            return null;
        }

        var foundUsers = new List<FullGuildUser>();
        var userPlays = new List<UserPlay>();

        var filter = GuildService.FilterGuildUsers(guildUsers, guild);

        foreach (var user in filter.FilteredGuildUsers.Where(w => w.Key != currentUserId))
        {
            var userFound = this._cache.TryGetValue($"{user.Key}-lastplay-album-{artistName.ToLower()}-{albumName.ToLower()}", out UserPlay userPlay);

            if (userFound)
            {
                foundUsers.Add(user.Value);
                userPlays.Add(userPlay);
            }
        }

        if (!foundUsers.Any())
        {
            return null;
        }

        return Description(foundUsers, userPlays, "album");
    }

    public string GuildAlsoPlayingArtist(
        int currentUserId,
        IDictionary<int, FullGuildUser> guildUsers,
        Persistence.Domain.Models.Guild guild,
        string artistName)
    {
        if (guild == null || guildUsers == null || !guildUsers.Any())
        {
            return null;
        }

        var foundUsers = new List<FullGuildUser>();
        var userPlays = new List<UserPlay>();

        var filter = GuildService.FilterGuildUsers(guildUsers, guild);

        foreach (var user in filter.FilteredGuildUsers.Where(w => w.Key != currentUserId))
        {
            var userFound = this._cache.TryGetValue($"{user.Key}-lastplay-artist-{artistName.ToLower()}", out UserPlay userPlay);

            if (userFound)
            {
                foundUsers.Add(user.Value);
                userPlays.Add(userPlay);
            }
        }

        if (!foundUsers.Any())
        {
            return null;
        }

        return Description(foundUsers, userPlays, "artist");
    }

    private static string Description(IReadOnlyList<FullGuildUser> fullGuildUsers, IEnumerable<UserPlay> userPlayTsList, string type)
    {
        return fullGuildUsers.Count switch
        {
            1 =>
                $"{fullGuildUsers.First().UserName} was also listening to this {type} {StringExtensions.GetTimeAgo(userPlayTsList.OrderByDescending(o => o.TimePlayed).First().TimePlayed)}!",
            2 =>
                $"{fullGuildUsers[0].UserName} and {fullGuildUsers[1].UserName} were also recently listening to this {type}!",
            3 =>
                $"{fullGuildUsers[0].UserName}, {fullGuildUsers[1].UserName} and {fullGuildUsers[2].UserName} were also recently listening to this {type}!",
            > 3 =>
                $"{fullGuildUsers[0].UserName}, {fullGuildUsers[1].UserName}, {fullGuildUsers[2].UserName} and {fullGuildUsers.Count - 3} others were also recently listening to this {type}!",
            _ => null
        };
    }
}
