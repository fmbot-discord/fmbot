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
    public class WhoKnowsTrackService
    {
        private readonly IDbContextFactory<FMBotDbContext> _contextFactory;

        public WhoKnowsTrackService(IDbContextFactory<FMBotDbContext> contextFactory)
        {
            this._contextFactory = contextFactory;
        }

        public async Task<IList<WhoKnowsObjectWithUser>> GetIndexedUsersForTrack(ICommandContext context,
            ICollection<GuildUser> guildUsers, string artistName, string trackName)
        {
            var userIds = guildUsers.Select(s => s.UserId);

            await using var db = this._contextFactory.CreateDbContext();
            var userTracks = await db.UserTracks
                .Include(i => i.User)
                .Where(w => EF.Functions.ILike(w.Name, trackName) &&
                            EF.Functions.ILike(w.ArtistName, artistName) &&
                            userIds.Contains(w.UserId))
                .OrderByDescending(o => o.Playcount)
                .Take(14)
                .ToListAsync();

            var whoKnowsTrackList = new List<WhoKnowsObjectWithUser>();

            foreach (var userTrack in userTracks)
            {
                var discordUser = await context.Guild.GetUserAsync(userTrack.User.DiscordUserId);
                var guildUser = guildUsers.FirstOrDefault(f => f.UserId == userTrack.UserId);
                var userName = discordUser != null ?
                    discordUser.Nickname ?? discordUser.Username :
                    guildUser?.UserName ?? userTrack.User.UserNameLastFM;

                whoKnowsTrackList.Add(new WhoKnowsObjectWithUser
                {
                    Name = $"{userTrack.ArtistName} - {userTrack.Name}",
                    DiscordName = userName,
                    Playcount = userTrack.Playcount,
                    LastFMUsername = userTrack.User.UserNameLastFM,
                    UserId = userTrack.UserId,
                });
            }

            return whoKnowsTrackList;
        }

        public async Task<IReadOnlyList<ListTrack>> GetTopTracksForGuild(IReadOnlyList<User> guildUsers,
            OrderType orderType)
        {
            var userIds = guildUsers.Select(s => s.UserId);

            await using var db = this._contextFactory.CreateDbContext();
            var query = db.UserTracks
                .AsQueryable()
                .Where(w => userIds.Contains(w.UserId))
                .GroupBy(g => new { g.ArtistName, g.Name });

            query = orderType == OrderType.Playcount ?
                query.OrderByDescending(o => o.Sum(s => s.Playcount)).ThenByDescending(o => o.Count()) :
                query.OrderByDescending(o => o.Count()).ThenByDescending(o => o.Sum(s => s.Playcount));

            return await query
                .Take(14)
                .Select(s => new ListTrack
                {
                    ArtistName = s.Key.ArtistName,
                    TrackName = s.Key.Name,
                    Playcount = s.Sum(su => su.Playcount),
                    ListenerCount = s.Count()
                })
                .ToListAsync();
        }

        public async Task<int> GetWeekTrackPlaycountForGuildAsync(IEnumerable<User> guildUsers, string trackName, string artistName)
        {
            var now = DateTime.UtcNow;
            var minDate = DateTime.UtcNow.AddDays(-7);

            var userIds = guildUsers.Select(s => s.UserId);

            await using var db = this._contextFactory.CreateDbContext();
            return await db.UserPlays
                .AsQueryable()
                .CountAsync(t => t.TimePlayed.Date <= now.Date &&
                                  t.TimePlayed.Date > minDate.Date &&
                                  t.TrackName.ToLower() == trackName.ToLower() &&
                                  t.ArtistName.ToLower() == artistName.ToLower() &&
                                  userIds.Contains(t.UserId));
        }
    }
}
