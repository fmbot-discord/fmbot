using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Discord.Commands;
using FMBot.Bot.Models;
using FMBot.Persistence.Domain.Models;
using FMBot.Persistence.EntityFrameWork;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace FMBot.Bot.Services.WhoKnows
{
    public class WhoKnowsAlbumService
    {
        private readonly IDbContextFactory<FMBotDbContext> _contextFactory;
        private readonly IMemoryCache _cache;

        public WhoKnowsAlbumService(IDbContextFactory<FMBotDbContext> contextFactory, IMemoryCache cache)
        {
            this._contextFactory = contextFactory;
            this._cache = cache;
        }

        public async Task<IList<WhoKnowsObjectWithUser>> GetIndexedUsersForAlbum(ICommandContext context,
            ICollection<GuildUser> guildUsers, string artistName, string albumName)
        {
            var cachedUserAlbums = new List<CachedUserAlbum>();
            foreach (var user in guildUsers)
            {
                var key = $"top-albums-{user.UserId}";

                if (this._cache.TryGetValue(key, out List<CachedUserAlbum> topAlbumsForUser))
                {
                    var userAlbum = topAlbumsForUser.FirstOrDefault(f => f.Name.ToLower() == albumName.ToLower() &&
                                                                          f.ArtistName.ToLower() == artistName.ToLower());

                    if (userAlbum != null)
                    {
                        cachedUserAlbums.Add(userAlbum);
                    }
                }
                else
                {
                    await using var db = this._contextFactory.CreateDbContext();
                    var userAlbumsToCache = await db.UserAlbums
                        .AsQueryable()
                        .Include(i => i.User)
                        .Where(w => w.UserId == user.UserId)
                        .Select(s => new CachedUserAlbum
                        {
                            Name = s.Name,
                            ArtistName = s.ArtistName,
                            Playcount = s.Playcount,
                            LastFmUserName = s.User.UserNameLastFM,
                            UserId = s.UserId,
                            DiscordUserId = s.User.DiscordUserId
                        })
                        .ToListAsync();

                    if (userAlbumsToCache != null && userAlbumsToCache.Any())
                    {
                        this._cache.Set(key, userAlbumsToCache, TimeSpan.FromMinutes(30));

                        var userAlbum = userAlbumsToCache.FirstOrDefault(f => f.Name.ToLower() == albumName.ToLower() &&
                                                                              f.ArtistName.ToLower() == artistName.ToLower());

                        if (userAlbum != null)
                        {
                            cachedUserAlbums.Add(userAlbum);
                        }
                    }
                }
            }

            var whoKnowsAlbumList = new List<WhoKnowsObjectWithUser>();

            foreach (var userAlbum in cachedUserAlbums)
            {
                var discordUser = await context.Guild.GetUserAsync(userAlbum.DiscordUserId);
                var guildUser = guildUsers.FirstOrDefault(f => f.UserId == userAlbum.UserId);
                var userName = discordUser != null ?
                    discordUser.Nickname ?? discordUser.Username :
                    guildUser?.UserName ?? userAlbum.LastFmUserName;

                whoKnowsAlbumList.Add(new WhoKnowsObjectWithUser
                {
                    Name = $"{userAlbum.ArtistName} - {userAlbum.Name}",
                    DiscordName = userName,
                    Playcount = userAlbum.Playcount,
                    LastFMUsername = userAlbum.LastFmUserName,
                    UserId = userAlbum.UserId,
                });
            }

            return whoKnowsAlbumList;
        }

        public async Task<IReadOnlyList<ListAlbum>> GetTopAlbumsForGuild(IReadOnlyList<User> guildUsers,
            OrderType orderType)
        {
            var userIds = guildUsers.Select(s => s.UserId);

            await using var db = this._contextFactory.CreateDbContext();
            var query = db.UserAlbums
                .AsQueryable()
                .Where(w => userIds.Contains(w.UserId))
                .GroupBy(g => new { g.ArtistName, g.Name });

            query = orderType == OrderType.Playcount ?
                query.OrderByDescending(o => o.Sum(s => s.Playcount)).ThenByDescending(o => o.Count()) :
                query.OrderByDescending(o => o.Count()).ThenByDescending(o => o.Sum(s => s.Playcount));

            return await query
                .Take(14)
                .Select(s => new ListAlbum
                {
                    ArtistName = s.Key.ArtistName,
                    AlbumName = s.Key.Name,
                    Playcount = s.Sum(su => su.Playcount),
                    ListenerCount = s.Count()
                })
                .ToListAsync();
        }

        public async Task<int> GetWeekAlbumPlaycountForGuildAsync(IEnumerable<User> guildUsers, string albumName, string artistName)
        {
            var now = DateTime.UtcNow;
            var minDate = DateTime.UtcNow.AddDays(-7);

            var userIds = guildUsers.Select(s => s.UserId);

            await using var db = this._contextFactory.CreateDbContext();
            return await db.UserPlays
                .AsQueryable()
                .CountAsync(ab =>
                                userIds.Contains(ab.UserId) &&
                                ab.TimePlayed.Date <= now.Date &&
                                ab.TimePlayed.Date > minDate.Date &&
                                ab.AlbumName.ToLower() == albumName.ToLower() &&
                                ab.ArtistName.ToLower() == artistName.ToLower());
        }
    }
}
