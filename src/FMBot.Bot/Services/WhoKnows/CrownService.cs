using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Discord.WebSocket;
using FMBot.Bot.Extensions;
using FMBot.Bot.Models;
using FMBot.Domain;
using FMBot.LastFM.Services;
using FMBot.Persistence.Domain.Models;
using FMBot.Persistence.EntityFrameWork;
using Microsoft.EntityFrameworkCore;

namespace FMBot.Bot.Services.WhoKnows
{
    public class CrownService
    {
        private readonly IDbContextFactory<FMBotDbContext> _contextFactory;
        private readonly LastFmService _lastFmService;

        public CrownService(IDbContextFactory<FMBotDbContext> contextFactory, LastFmService lastFmService)
        {
            this._contextFactory = contextFactory;
            this._lastFmService = lastFmService;
        }

        public async Task<CrownModel> GetAndUpdateCrownForArtist(IList<WhoKnowsObjectWithUser> users, Guild guild, string artistName)
        {
            var eligibleUsers = guild.GuildUsers.ToList();

            if (guild.CrownsActivityThresholdDays.HasValue)
            {
                eligibleUsers = eligibleUsers.Where(w =>
                        w.User.LastUsed != null &&
                        w.User.LastUsed >= DateTime.UtcNow.AddDays(-guild.CrownsActivityThresholdDays.Value))
                    .ToList();
            }
            if (guild.GuildBlockedUsers != null && guild.GuildBlockedUsers.Any(a => a.BlockedFromCrowns))
            {
                eligibleUsers = eligibleUsers.Where(w =>
                        !guild.GuildBlockedUsers
                            .Where(wh => wh.BlockedFromCrowns)
                            .Select(s => s.UserId).Contains(w.UserId))
                    .ToList();
            }

            var topUser = users
                .Where(w => eligibleUsers.Select(s => s.UserId).Contains(w.UserId))
                .OrderByDescending(o => o.Playcount)
                .FirstOrDefault();

            if (topUser == null)
            {
                return null;
            }

            await using var db = this._contextFactory.CreateDbContext();
            var currentCrownHolder = await db.UserCrowns
                .AsQueryable()
                .Include(i => i.User)
                .OrderByDescending(o => o.CurrentPlaycount)
                .FirstOrDefaultAsync(f => f.GuildId == guild.GuildId &&
                                          f.Active &&
                                          EF.Functions.ILike(f.ArtistName, artistName));

            // Crown exists and is same as top user
            if (currentCrownHolder != null && topUser.UserId == currentCrownHolder.UserId)
            {
                var oldPlaycount = currentCrownHolder.CurrentPlaycount;
                if (oldPlaycount < topUser.Playcount)
                {
                    currentCrownHolder.CurrentPlaycount = topUser.Playcount;
                    currentCrownHolder.Modified = DateTime.UtcNow;

                    db.Entry(currentCrownHolder).State = EntityState.Modified;
                    await db.SaveChangesAsync();

                    return new CrownModel
                    {
                        Crown = currentCrownHolder
                    };
                }

                return new CrownModel
                {
                    Crown = currentCrownHolder,
                };
            }

            // Crown exists, but top user is a different person
            if (currentCrownHolder != null && topUser.UserId != currentCrownHolder.UserId)
            {
                var currentPlaycountForCrownHolder =
                    await GetCurrentPlaycountForUser(artistName, currentCrownHolder.User.UserNameLastFM);

                if (currentPlaycountForCrownHolder == null)
                {
                    return new CrownModel
                    {
                        Crown = currentCrownHolder,
                        CrownResult = $"Could not confirm playcount for current crown holder."
                    };
                }
                if (eligibleUsers.Select(s => s.UserId).Contains(currentCrownHolder.UserId) && currentPlaycountForCrownHolder >= topUser.Playcount)
                {
                    var oldPlaycount = currentCrownHolder.CurrentPlaycount;
                    currentCrownHolder.CurrentPlaycount = topUser.Playcount;
                    currentCrownHolder.Modified = DateTime.UtcNow;

                    db.Entry(currentCrownHolder).State = EntityState.Modified;

                    await db.SaveChangesAsync();

                    return new CrownModel
                    {
                        Crown = currentCrownHolder
                    };
                }

                if (currentCrownHolder.CurrentPlaycount != currentPlaycountForCrownHolder)
                {
                    currentCrownHolder.CurrentPlaycount = topUser.Playcount;
                }

                currentCrownHolder.Active = false;
                currentCrownHolder.Modified = DateTime.UtcNow;

                db.Entry(currentCrownHolder).State = EntityState.Modified;

                var newCrown = new UserCrown
                {
                    UserId = topUser.UserId,
                    GuildId = guild.GuildId,
                    ArtistName = artistName,
                    Active = true,
                    Created = DateTime.UtcNow,
                    Modified = DateTime.UtcNow,
                    StartPlaycount = topUser.Playcount,
                    CurrentPlaycount = topUser.Playcount
                };

                await db.UserCrowns.AddAsync(newCrown);

                await db.SaveChangesAsync();

                return new CrownModel
                {
                    Crown = newCrown,
                    CrownResult = $"Crown stolen by {topUser.DiscordName}! (From **{currentCrownHolder.CurrentPlaycount}** to **{topUser.Playcount}** plays)."
                };
            }

            var minAmountOfPlaysForCrown = guild.CrownsMinimumPlaycountThreshold ?? Constants.DefaultPlaysForCrown;

            // No crown exists yet
            if (currentCrownHolder == null)
            {
                if (topUser.Playcount >= minAmountOfPlaysForCrown)
                {
                    var newCrown = new UserCrown
                    {
                        UserId = topUser.UserId,
                        GuildId = guild.GuildId,
                        ArtistName = artistName,
                        Active = true,
                        Created = DateTime.UtcNow,
                        Modified = DateTime.UtcNow,
                        StartPlaycount = topUser.Playcount,
                        CurrentPlaycount = topUser.Playcount
                    };

                    await db.UserCrowns.AddAsync(newCrown);

                    await db.SaveChangesAsync();

                    return new CrownModel
                    {
                        Crown = newCrown,
                        CrownResult = $"Crown claimed by {topUser.DiscordName}!"
                    };
                }

                if (topUser.Playcount >= minAmountOfPlaysForCrown / 3)
                {
                    var amountOfPlaysRequired = minAmountOfPlaysForCrown - topUser.Playcount;

                    return new CrownModel
                    {
                        CrownResult = $"{topUser.DiscordName} needs {amountOfPlaysRequired} more {StringExtensions.GetPlaysString(amountOfPlaysRequired)} to claim the crown."
                    };
                }
            }

            return null;
        }

        private async Task<long?> GetCurrentPlaycountForUser(string artistName, string lastFmUserName)
        {
            var artist = await this._lastFmService.GetArtistInfoAsync(artistName, lastFmUserName);

            if (!artist.Success)
            {
                return null;
            }

            return artist.Content.Artist.Stats.Userplaycount;
        }

        public async Task<IList<UserCrown>> GetCrownsForArtist(Guild guild, string artistName)
        {
            await using var db = this._contextFactory.CreateDbContext();
            return await db.UserCrowns
                .AsQueryable()
                .Include(i => i.User)
                .OrderByDescending(o => o.CurrentPlaycount)
                .Where(f => f.GuildId == guild.GuildId &&
                                          EF.Functions.ILike(f.ArtistName, artistName))
                .ToListAsync();
        }

        public async Task RemoveCrowns(IList<UserCrown> crowns)
        {
            await using var db = this._contextFactory.CreateDbContext();

            db.UserCrowns.RemoveRange(crowns);

            await db.SaveChangesAsync();
        }

        public async Task<IList<UserCrown>> GetCrownsForUser(Guild guild, int userId)
        {
            await using var db = this._contextFactory.CreateDbContext();
            return await db.UserCrowns
                .AsQueryable()
                .Include(i => i.User)
                .OrderByDescending(o => o.CurrentPlaycount)
                .Where(f => f.GuildId == guild.GuildId &&
                            f.Active &&
                            f.UserId == userId)
                .ToListAsync();
        }

        public async Task<List<IGrouping<int, UserCrown>>> GetTopCrownUsersForGuild(Guild guild)
        {
            await using var db = this._contextFactory.CreateDbContext();
            var guildCrowns = await db.UserCrowns
                .AsQueryable()
                .Include(i => i.User)
                .Where(f => f.GuildId == guild.GuildId &&
                            f.Active)
                .ToListAsync();

            return guildCrowns
                .GroupBy(g => g.UserId)
                .OrderByDescending(o => o.Count())
                .ToList();
        }

        public async Task<int> GetTotalCrownCountForGuild(Guild guild)
        {
            await using var db = this._contextFactory.CreateDbContext();
            return await db.UserCrowns
                .AsQueryable()
                .Include(i => i.User)
                .Where(f => f.GuildId == guild.GuildId &&
                            f.Active)
                .CountAsync();
        }

        public async Task RemoveAllCrownsFromGuild(Guild guild)
        {
            await using var db = this._contextFactory.CreateDbContext();
            var guildCrowns = await db.UserCrowns
                .AsQueryable()
                .Where(f => f.GuildId == guild.GuildId)
                .ToListAsync();

            db.UserCrowns.RemoveRange(guildCrowns);

            await db.SaveChangesAsync();
        }

        public async Task RemoveAllCrownsFromUser(int userId)
        {
            await using var db = this._contextFactory.CreateDbContext();
            var userCrowns = await db.UserCrowns
                .AsQueryable()
                .Where(f => f.UserId == userId)
                .ToListAsync();

            db.UserCrowns.RemoveRange(userCrowns);

            await db.SaveChangesAsync();
        }

        public async Task RemoveAllCrownsFromDiscordUser(SocketGuildUser user)
        {
            await using var db = this._contextFactory.CreateDbContext();
            var userThatLeft = await db.Users
                .AsQueryable()
                .FirstOrDefaultAsync(f => f.DiscordUserId == user.Id);

            if (userThatLeft == null)
            {
                return;
            }

            var guild = await db.Guilds
                .Include(i => i.GuildUsers)
                .Include(i => i.GuildCrowns)
                .FirstOrDefaultAsync(f => f.DiscordGuildId == user.Guild.Id);

            if (guild.GuildCrowns != null && guild.GuildCrowns.Any())
            {
                var userGuildCrowns = await db.UserCrowns
                    .AsQueryable()
                    .Where(f => f.UserId == userThatLeft.UserId &&
                                f.GuildId == guild.GuildId)
                    .ToListAsync();

                db.UserCrowns.RemoveRange(userGuildCrowns);

                await db.SaveChangesAsync();
            }
        }

    }
}
