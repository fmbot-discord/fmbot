using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Discord.WebSocket;
using FMBot.Bot.Models;
using FMBot.Domain;
using FMBot.LastFM.Services;
using FMBot.Persistence.Domain.Models;
using FMBot.Persistence.EntityFrameWork;
using Microsoft.EntityFrameworkCore;
using Serilog;

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
                eligibleUsers = guild.GuildUsers.Where(w =>
                        w.User.LastUsed != null &&
                        w.User.LastUsed >= DateTime.UtcNow.AddDays(-guild.CrownsActivityThresholdDays.Value))
                    .ToList();
            }
            if (guild.GuildBlockedUsers != null && guild.GuildBlockedUsers.Any(a => a.BlockedFromCrowns))
            {
                eligibleUsers = guild.GuildUsers.Where(w =>
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
            var currentCrownholder = await db.UserCrowns
                .AsQueryable()
                .Include(i => i.User)
                .OrderByDescending(o => o.CurrentPlaycount)
                .FirstOrDefaultAsync(f => f.GuildId == guild.GuildId &&
                                          f.Active &&
                                          EF.Functions.ILike(f.ArtistName, artistName));

            // Crown exists and is same as top user
            if (currentCrownholder != null && topUser.UserId == currentCrownholder.UserId)
            {
                var oldPlaycount = currentCrownholder.CurrentPlaycount;
                if (oldPlaycount < topUser.Playcount)
                {
                    currentCrownholder.CurrentPlaycount = topUser.Playcount;
                    currentCrownholder.Modified = DateTime.UtcNow;

                    db.Entry(currentCrownholder).State = EntityState.Modified;
                    await db.SaveChangesAsync();

                    return new CrownModel
                    {
                        Crown = currentCrownholder,
                        CrownResult = $"Crown playcount for {topUser.DiscordName} updated from {oldPlaycount} to {topUser.Playcount}."
                    };
                }

                return new CrownModel
                {
                    Crown = currentCrownholder,
                };
            }

            // Crown exists, but top user is a different person
            if (currentCrownholder != null && topUser.UserId != currentCrownholder.UserId)
            {
                var currentPlaycountForCrownHolder =
                    await GetCurrentPlaycountForUser(artistName, currentCrownholder.User.UserNameLastFM);

                if (currentPlaycountForCrownHolder == null)
                {
                    return new CrownModel
                    {
                        Crown = currentCrownholder,
                        CrownResult = $"Could not confirm playcount for current crown holder."
                    };
                }
                if (eligibleUsers.Select(s => s.UserId).Contains(currentCrownholder.UserId) && currentPlaycountForCrownHolder >= topUser.Playcount)
                {
                    var oldPlaycount = currentCrownholder.CurrentPlaycount;
                    currentCrownholder.CurrentPlaycount = topUser.Playcount;
                    currentCrownholder.Modified = DateTime.UtcNow;

                    db.Entry(currentCrownholder).State = EntityState.Modified;

                    await db.SaveChangesAsync();

                    return new CrownModel
                    {
                        Crown = currentCrownholder,
                        CrownResult = $"Crown playcount for {topUser.DiscordName} updated from {oldPlaycount} to {topUser.Playcount}."
                    };
                }

                if (currentCrownholder.CurrentPlaycount != currentPlaycountForCrownHolder)
                {
                    currentCrownholder.CurrentPlaycount = topUser.Playcount;
                }

                currentCrownholder.Active = false;
                currentCrownholder.Modified = DateTime.UtcNow;

                db.Entry(currentCrownholder).State = EntityState.Modified;

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
                    CrownResult = $"Crown stolen by {topUser.DiscordName}! ({currentCrownholder.CurrentPlaycount} > {topUser.Playcount} plays)."
                };
            }

            // No crown exists yet
            if (currentCrownholder == null)
            {
                if (topUser.Playcount >= Constants.MinPlaysForCrown)
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

                if (topUser.Playcount >= 10)
                {
                    var amountOfPlaysRequired = Constants.MinPlaysForCrown - topUser.Playcount;

                    return new CrownModel
                    {
                        CrownResult = $"{topUser.DiscordName} needs {amountOfPlaysRequired} more plays to claim the crown."
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
                .Where(f => f.GuildId == guild.GuildId)
                .ToListAsync();

            return guildCrowns
                .GroupBy(g => g.UserId)
                .OrderByDescending(o => o.Count())
                .ToList();
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
                .Include(i  => i.GuildCrowns)
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
