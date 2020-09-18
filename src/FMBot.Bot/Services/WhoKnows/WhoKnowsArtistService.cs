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
    public class WhoKnowsArtistService
    {
        public async Task<IList<WhoKnowsObjectWithUser>> GetIndexedUsersForArtist(ICommandContext context,
            IReadOnlyList<User> guildUsers, string artistName)
        {
            var userIds = guildUsers.Select(s => s.UserId);

            await using var db = new FMBotDbContext(ConfigData.Data.Database.ConnectionString);
            var artists = await db.UserArtists
                .Include(i => i.User)
                .Where(w => w.Name.ToLower() == artistName.ToLower()
                            && userIds.Contains(w.UserId))
                .OrderByDescending(o => o.Playcount)
                .Take(14)
                .ToListAsync();

            var whoKnowsArtistList = new List<WhoKnowsObjectWithUser>();

            foreach (var artist in artists)
            {
                var discordUser = await context.Guild.GetUserAsync(artist.User.DiscordUserId);
                if (discordUser != null)
                {
                    whoKnowsArtistList.Add(new WhoKnowsObjectWithUser
                    {
                        Name = artist.Name,
                        DiscordName = discordUser.Nickname ?? discordUser.Username,
                        Playcount = artist.Playcount,
                        DiscordUserId = artist.User.DiscordUserId,
                        LastFMUsername = artist.User.UserNameLastFM,
                        UserId = artist.UserId
                    });
                }
            }

            return whoKnowsArtistList;
        }

        public async Task<IList<ListArtist>> GetTopArtistsForGuild(IReadOnlyList<User> guildUsers)
        {
            var userIds = guildUsers.Select(s => s.UserId);

            await using var db = new FMBotDbContext(ConfigData.Data.Database.ConnectionString);
            return await db.UserArtists
                .AsQueryable()
                .Where(w => userIds.Contains(w.UserId))
                .GroupBy(o => o.Name)
                .OrderByDescending(o => o.Sum(s => s.Playcount))
                .Take(14)
                .Select(s => new ListArtist
                {
                    ArtistName = s.Key,
                    Playcount = s.Sum(s => s.Playcount),
                    ListenerCount = s.Count()
                })
                .ToListAsync();
        }

        public async Task<int> GetArtistListenerCountForServer(IReadOnlyList<User> guildUsers, string artistName)
        {
            var userIds = guildUsers.Select(s => s.UserId);

            await using var db = new FMBotDbContext(ConfigData.Data.Database.ConnectionString);
            return await db.UserArtists
                .AsQueryable()
                .Where(w => w.Name.ToLower() == artistName.ToLower()
                            && userIds.Contains(w.UserId))
                .CountAsync();
        }

        public async Task<int> GetArtistPlayCountForServer(IReadOnlyList<User> guildUsers, string artistName)
        {
            var userIds = guildUsers.Select(s => s.UserId);

            await using var db = new FMBotDbContext(ConfigData.Data.Database.ConnectionString);
            var query = db.UserArtists
                .AsQueryable()
                .Where(w => w.Name.ToLower() == artistName.ToLower()
                            && userIds.Contains(w.UserId));

            // This is bad practice, but it helps with speed. An exception gets thrown if the artist does not exist in the database.
            // Checking if the records exist first would be an extra database call
            try
            {
                return await query.SumAsync(s => s.Playcount);
            }
            catch
            {
                return 0;
            }
        }

        public async Task<double> GetArtistAverageListenerPlaycountForServer(IReadOnlyList<User> guildUsers, string artistName)
        {
            var userIds = guildUsers.Select(s => s.UserId);

            await using var db = new FMBotDbContext(ConfigData.Data.Database.ConnectionString);
            var query = db.UserArtists
                .AsQueryable()
                .Where(w => w.Name.ToLower() == artistName.ToLower()
                            && userIds.Contains(w.UserId));

            try
            {
                return await query.AverageAsync(s => s.Playcount);
            }
            catch
            {
                return 0;
            }
        }

        public async Task<int> GetWeekArtistPlaycountForGuildAsync(IEnumerable<User> guildUsers, string artistName)
        {
            var now = DateTime.UtcNow;
            var minDate = DateTime.UtcNow.AddDays(-7);

            var userIds = guildUsers.Select(s => s.UserId);

            await using var db = new FMBotDbContext(ConfigData.Data.Database.ConnectionString);
            return await db.UserPlays
                .AsQueryable()
                .CountAsync(a => a.TimePlayed.Date <= now.Date &&
                                  a.TimePlayed.Date > minDate.Date &&
                                  a.ArtistName.ToLower() == artistName.ToLower() &&
                                  userIds.Contains(a.UserId));
        }
    }
}
