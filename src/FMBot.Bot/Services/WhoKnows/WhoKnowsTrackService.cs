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
    public class WhoKnowsTrackService
    {
        public async Task<IList<WhoKnowsObjectWithUser>> GetIndexedUsersForTrack(ICommandContext context,
            IReadOnlyList<User> guildUsers, string artistName, string trackName)
        {
            var userIds = guildUsers.Select(s => s.UserId);

            await using var db = new FMBotDbContext(ConfigData.Data.Database.ConnectionString);
            var tracks = await db.UserTracks
                .Include(i => i.User)
                .Where(w => w.Name.ToLower() == trackName.ToLower() &&
                            w.ArtistName.ToLower() == artistName.ToLower()
                            && userIds.Contains(w.UserId))
                .OrderByDescending(o => o.Playcount)
                .Take(14)
                .ToListAsync();

            var whoKnowsTrackList = new List<WhoKnowsObjectWithUser>();

            foreach (var track in tracks)
            {
                var discordUser = await context.Guild.GetUserAsync(track.User.DiscordUserId);
                if (discordUser != null)
                {
                    whoKnowsTrackList.Add(new WhoKnowsObjectWithUser
                    {
                        Name = $"{track.ArtistName} - {track.Name}",
                        DiscordName = discordUser.Nickname ?? discordUser.Username,
                        Playcount = track.Playcount,
                        DiscordUserId = track.User.DiscordUserId,
                        LastFMUsername = track.User.UserNameLastFM,
                        UserId = track.UserId,
                    });
                }
            }

            return whoKnowsTrackList;
        }

        public async Task<IList<ListArtist>> GetTopTracksForGuild(IReadOnlyList<User> guildUsers)
        {
            var userIds = guildUsers.Select(s => s.UserId);

            await using var db = new FMBotDbContext(ConfigData.Data.Database.ConnectionString);
            return await db.UserTracks
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

        public async Task<int> GetWeekTrackPlaycountForGuildAsync(IEnumerable<User> guildUsers, string trackName, string artistName)
        {
            var now = DateTime.UtcNow;
            var minDate = DateTime.UtcNow.AddDays(-7);

            var userIds = guildUsers.Select(s => s.UserId);

            await using var db = new FMBotDbContext(ConfigData.Data.Database.ConnectionString);
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
