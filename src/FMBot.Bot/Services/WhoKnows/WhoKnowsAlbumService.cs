using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Discord.Commands;
using FMBot.Bot.Models;
using FMBot.Persistence.Domain.Models;
using FMBot.Persistence.EntityFrameWork;
using Microsoft.EntityFrameworkCore;

namespace FMBot.Bot.Services.WhoKnows
{
    public class WhoKnowsAlbumService
    {
        private readonly IDbContextFactory<FMBotDbContext> _contextFactory;

        public WhoKnowsAlbumService(IDbContextFactory<FMBotDbContext> contextFactory)
        {
            this._contextFactory = contextFactory;
        }

        public async Task<IList<WhoKnowsObjectWithUser>> GetIndexedUsersForAlbum(ICommandContext context,
            ICollection<GuildUser> guildUsers, string artistName, string albumName)
        {
            var userIds = guildUsers.Select(s => s.UserId);

            await using var db = this._contextFactory.CreateDbContext();
            var userAlbums = await db.UserAlbums
                .Include(i => i.User)
                .Where(w =>
                        EF.Functions.ILike(w.Name, albumName) &&
                        EF.Functions.ILike(w.ArtistName, artistName) &&
                        userIds.Contains(w.UserId))
                .OrderByDescending(o => o.Playcount)
                .Take(14)
                .ToListAsync();

            var whoKnowsAlbumList = new List<WhoKnowsObjectWithUser>();

            foreach (var userAlbum in userAlbums)
            {
                var discordUser = await context.Guild.GetUserAsync(userAlbum.User.DiscordUserId);
                var guildUser = guildUsers.FirstOrDefault(f => f.UserId == userAlbum.UserId);
                var userName = discordUser != null ?
                    discordUser.Nickname ?? discordUser.Username :
                    guildUser?.UserName ?? userAlbum.User.UserNameLastFM;

                whoKnowsAlbumList.Add(new WhoKnowsObjectWithUser
                {
                    Name = $"{userAlbum.ArtistName} - {userAlbum.Name}",
                    DiscordName = userName,
                    Playcount = userAlbum.Playcount,
                    LastFMUsername = userAlbum.User.UserNameLastFM,
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
                .CountAsync(ab => ab.TimePlayed.Date <= now.Date &&
                                 ab.TimePlayed.Date > minDate.Date &&
                                 ab.AlbumName.ToLower() == albumName.ToLower() &&
                                 ab.ArtistName.ToLower() == artistName.ToLower() &&
                                 userIds.Contains(ab.UserId));
        }
    }
}
