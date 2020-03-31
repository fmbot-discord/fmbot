using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dasync.Collections;
using Discord.Commands;
using FMBot.Bot.Configurations;
using FMBot.Bot.Resources;
using FMBot.Data;
using FMBot.Data.Entities;
using IF.Lastfm.Core.Api;
using IF.Lastfm.Core.Api.Enums;
using Microsoft.EntityFrameworkCore;

namespace FMBot.Bot.Services
{
    public class IndexService
    {
        private readonly LastfmClient _lastFMClient = new LastfmClient(ConfigData.Data.FMKey, ConfigData.Data.FMSecret);

        private readonly FMBotDbContext _db = new FMBotDbContext();

        private const int MaxRequestsPerMinute = 35;

        public async Task<bool> IndexGuild(IReadOnlyList<User> users)
        {
            var requestsInLastMinute = 0;
            var failedRequests = 0;
            var lastMinute = DateTime.Now.AddSeconds(users.Count).Minute;

            await users.ParallelForEachAsync(async user =>
            {
                while (requestsInLastMinute >= MaxRequestsPerMinute)
                {
                    if (DateTime.Now.AddSeconds(users.Count).Minute != lastMinute)
                    {
                        requestsInLastMinute = 0;
                        lastMinute = DateTime.Now.AddSeconds(users.Count).Minute;
                        break;
                    }

                    Thread.Sleep(1000);
                }

                var succes = await StoreArtistsForUser(user);
                requestsInLastMinute++;

                if (!succes)
                {
                    failedRequests++;
                }
            }, maxDegreeOfParallelism: 6);

            return true;
        }

        private async Task<bool> StoreArtistsForUser(User user)
        {
            var topArtists = await this._lastFMClient.User.GetTopArtists(user.UserNameLastFM, LastStatsTimeSpan.Overall, 1, 1000);
            Statistics.LastfmApiCalls.Inc();

            var now = DateTime.UtcNow;
            user.Artists = topArtists.Select(a => new Artist
            {
                LastUpdated = now,
                Name = a.Name,
                Playcount = a.PlayCount.Value,
                UserId = user.UserId
            }).ToList();

            user.LastIndexed = now;

            this._db.Users.Update(user);

            await this._db.SaveChangesAsync();

            return true;
        }

        public async Task<IReadOnlyList<User>> GetUsersForContext(ICommandContext context)
        {
            var users = await context.Guild.GetUsersAsync();

            var userIds = users.Select(s => s.Id).ToList();

            var tooRecent = DateTime.UtcNow.Add(-Constants.GuildIndexCooldown);
            return await this._db.Users
                .Include(i => i.Artists)
                .Where(w => userIds.Contains(w.DiscordUserId)
                && w.LastIndexed == null || w.LastIndexed <= tooRecent)
                .ToListAsync();
        }
    }
}
