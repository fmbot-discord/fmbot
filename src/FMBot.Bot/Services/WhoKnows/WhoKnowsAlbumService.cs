using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Discord.Commands;
using FMBot.Bot.Configurations;
using FMBot.Bot.Models;
using FMBot.Persistence.Domain.Models;
using FMBot.Persistence.EntityFrameWork;
using Microsoft.EntityFrameworkCore;

namespace FMBot.Bot.Services.WhoKnows
{
    public class WhoKnowsAlbumService
    {
        public async Task<IList<WhoKnowsObjectWithUser>> GetIndexedUsersForAlbum(ICommandContext context,
            IReadOnlyList<User> guildUsers, string artistName, string albumName)
        {
            var userIds = guildUsers.Select(s => s.UserId);

            await using var db = new FMBotDbContext(ConfigData.Data.Database.ConnectionString);
            var albums = await db.UserAlbums
                .Include(i => i.User)
                .Where(w => w.Name.ToLower() == albumName.ToLower() &&
                            w.ArtistName.ToLower() == artistName.ToLower()
                            && userIds.Contains(w.UserId))
                .OrderByDescending(o => o.Playcount)
                .Take(14)
                .ToListAsync();

            var whoKnowsAlbumList = new List<WhoKnowsObjectWithUser>();

            foreach (var album in albums)
            {
                var discordUser = await context.Guild.GetUserAsync(album.User.DiscordUserId);
                if (discordUser != null)
                {
                    whoKnowsAlbumList.Add(new WhoKnowsObjectWithUser
                    {
                        Name = $"{album.ArtistName} - {album.Name}",
                        DiscordName = discordUser.Nickname ?? discordUser.Username,
                        Playcount = album.Playcount,
                        DiscordUserId = album.User.DiscordUserId,
                        LastFMUsername = album.User.UserNameLastFM,
                        UserId = album.UserId,
                    });
                }
            }

            return whoKnowsAlbumList;
        }

        public async Task<IReadOnlyList<ListAlbum>> GetTopAlbumsForGuild(IReadOnlyList<User> guildUsers,
            OrderType orderType)
        {
            var userIds = guildUsers.Select(s => s.UserId);

            await using var db = new FMBotDbContext(ConfigData.Data.Database.ConnectionString);
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

            await using var db = new FMBotDbContext(ConfigData.Data.Database.ConnectionString);
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
