using System.Linq;
using System.Collections.Generic;
using FMBot.Bot.Models;
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
        Localizer localizer,
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
            var userFound = this._cache.TryGetValue($"{user.Key}-lp-track-{artistName.ToLower()}-{trackName.ToLower()}", out UserPlay userPlay);

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

        return Description(localizer, foundUsers, userPlays, "Track");
    }

    public string GuildAlsoPlayingAlbum(
        Localizer localizer,
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
            var userFound = this._cache.TryGetValue($"{user.Key}-lp-album-{artistName.ToLower()}-{albumName.ToLower()}", out UserPlay userPlay);

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

        return Description(localizer, foundUsers, userPlays, "Album");
    }

    public string GuildAlsoPlayingArtist(
        Localizer localizer,
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
            var userFound = this._cache.TryGetValue($"{user.Key}-lp-artist-{artistName.ToLower()}", out UserPlay userPlay);

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

        return Description(localizer, foundUsers, userPlays, "Artist");
    }

    private static string Description(Localizer localizer, IReadOnlyList<FullGuildUser> fullGuildUsers, IEnumerable<UserPlay> userPlayTsList, string type)
    {
        return fullGuildUsers.Count switch
        {
            1 =>
                localizer.Translate($"whoknows.alsoPlayingOne{type}",
                    ("user", fullGuildUsers.First().UserName),
                    ("timeAgo", localizer.TimeAgo(userPlayTsList.OrderByDescending(o => o.TimePlayed).First().TimePlayed))),
            2 =>
                localizer.Translate($"whoknows.alsoPlayingTwo{type}s",
                    ("userOne", fullGuildUsers[0].UserName),
                    ("userTwo", fullGuildUsers[1].UserName)),
            3 =>
                localizer.Translate($"whoknows.alsoPlayingThree{type}s",
                    ("userOne", fullGuildUsers[0].UserName),
                    ("userTwo", fullGuildUsers[1].UserName),
                    ("userThree", fullGuildUsers[2].UserName)),
            > 3 =>
                localizer.TranslateCount($"whoknows.alsoPlayingMany{type}s", fullGuildUsers.Count - 3,
                    ("userOne", fullGuildUsers[0].UserName),
                    ("userTwo", fullGuildUsers[1].UserName),
                    ("userThree", fullGuildUsers[2].UserName)),
            _ => null
        };
    }
}
