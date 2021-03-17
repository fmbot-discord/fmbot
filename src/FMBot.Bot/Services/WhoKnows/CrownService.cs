using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using Discord.WebSocket;
using FMBot.Bot.Configurations;
using FMBot.Bot.Extensions;
using FMBot.Bot.Models;
using FMBot.Domain;
using FMBot.Domain.Models;
using FMBot.LastFM.Services;
using FMBot.Persistence.Domain.Models;
using FMBot.Persistence.EntityFrameWork;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PostgreSQLCopyHelper;

namespace FMBot.Bot.Services.WhoKnows
{
    public class CrownService
    {
        private readonly IDbContextFactory<FMBotDbContext> _contextFactory;
        private readonly LastFmService _lastFmService;
        private readonly GlobalUpdateService _globalUpdateService;

        public CrownService(IDbContextFactory<FMBotDbContext> contextFactory, LastFmService lastFmService, GlobalUpdateService globalUpdateService)
        {
            this._contextFactory = contextFactory;
            this._lastFmService = lastFmService;
            this._globalUpdateService = globalUpdateService;
        }

        public async Task<CrownModel> GetAndUpdateCrownForArtist(IList<WhoKnowsObjectWithUser> users, Persistence.Domain.Models.Guild guild, string artistName)
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
                    currentCrownHolder.SeededCrown = false;

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
                    await GetCurrentPlaycountForUser(artistName, currentCrownHolder.User.UserNameLastFM, currentCrownHolder.UserId);

                if (PublicProperties.IssuesAtLastFm)
                {
                    return new CrownModel
                    {
                        Crown = currentCrownHolder,
                        CrownResult = "*Crown stealing is currently disabled due to issues with the Last.fm API*"
                    };
                }
                if (currentPlaycountForCrownHolder == null)
                {
                    return new CrownModel
                    {
                        Crown = currentCrownHolder,
                        CrownResult = "Could not confirm playcount for current crown holder."
                    };
                }
                if (eligibleUsers.Select(s => s.UserId).Contains(currentCrownHolder.UserId) && currentPlaycountForCrownHolder >= topUser.Playcount)
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
                    CurrentPlaycount = topUser.Playcount,
                    SeededCrown = false
                };

                await db.UserCrowns.AddAsync(newCrown);

                await db.SaveChangesAsync();

                var currentCrownHolderName = users.FirstOrDefault(f => f.UserId == currentCrownHolder.UserId)?.DiscordName;

                return new CrownModel
                {
                    Crown = newCrown,
                    CrownResult = $"Crown stolen by {topUser.DiscordName} with `{topUser.Playcount}` plays! \n" +
                                  $"*Previous owner: {currentCrownHolderName ?? currentCrownHolder.User.UserNameLastFM} with `{currentCrownHolder.CurrentPlaycount}` plays*."
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
                        CurrentPlaycount = topUser.Playcount,
                        SeededCrown = false
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

        public async Task<int> SeedCrownsForGuild(Persistence.Domain.Models.Guild guild, IList<UserCrown> existingCrowns)
        {
            const string sql = "SELECT DISTINCT ON(ua.name) " +
                                   "ua.user_id, " +
                                   "ua.name, " +
                                   "ua.playcount " +
                                   "FROM user_artists AS ua " +
                                   "INNER JOIN users AS u ON ua.user_id = u.user_id " +
                                   "INNER JOIN guild_users AS gu ON gu.user_id = u.user_id " +
                                   "WHERE gu.guild_id = @guildId AND playcount > @minPlaycount " +
                                   "AND NOT ua.user_id = ANY(SELECT user_id FROM guild_blocked_users WHERE blocked_from_crowns = true AND guild_id = @guildId) " +
                                   "ORDER BY ua.name, ua.playcount DESC;";

            DefaultTypeMap.MatchNamesWithUnderscores = true;
            await using var connection = new NpgsqlConnection(ConfigData.Data.Database.ConnectionString);
            await connection.OpenAsync();

            var minPlaycount = guild.CrownsMinimumPlaycountThreshold ?? Constants.DefaultPlaysForCrown;

            var topUsersForPlaycount = await connection.QueryAsync<CrownSeedDto>(sql, new
            {
                guild.GuildId,
                minPlaycount
            });

            var now = DateTime.UtcNow;
            var crownsToSeed = topUsersForPlaycount.Select(s =>
                new UserCrown
                {
                    UserId = s.UserId,
                    Active = true,
                    ArtistName = s.Name,
                    Created = existingCrowns.FirstOrDefault(f => f.UserId == s.UserId && f.ArtistName.ToLower() == s.Name.ToLower())?.Created ?? now,
                    Modified = now,
                    CurrentPlaycount = s.Playcount,
                    StartPlaycount = existingCrowns.FirstOrDefault(f => f.UserId == s.UserId && f.ArtistName.ToLower() == s.Name.ToLower())?.StartPlaycount ?? s.Playcount,
                    GuildId = guild.GuildId,
                    SeededCrown = true
                }).ToList();

            if (existingCrowns != null && existingCrowns.Any())
            {
                crownsToSeed = crownsToSeed.Where(w =>
                        !existingCrowns
                            .Where(wh => !wh.SeededCrown)
                            .Select(s => s.ArtistName.ToLower())
                            .Contains(w.ArtistName.ToLower()))
                    .ToList();
            }
            const string deleteOldSeededCrownsSql = "DELETE FROM public.user_crowns " +
                                                    "WHERE guild_id = @guildId AND seeded_crown = true;";

            await connection.ExecuteAsync(deleteOldSeededCrownsSql, new
            {
                guild.GuildId,
            });

            var copyHelper = new PostgreSQLCopyHelper<UserCrown>("public", "user_crowns")
                .MapInteger("guild_id", x => x.GuildId)
                .MapInteger("user_id", x => x.UserId)
                .MapText("artist_name", x => x.ArtistName)
                .MapInteger("current_playcount", x => x.CurrentPlaycount)
                .MapInteger("start_playcount", x => x.StartPlaycount)
                .MapTimeStamp("created", x => x.Created)
                .MapTimeStamp("modified", x => x.Created)
                .MapBoolean("active", x => x.Active)
                .MapBoolean("seeded_crown", x => x.SeededCrown);

            await copyHelper.SaveAllAsync(connection, crownsToSeed);
            await connection.CloseAsync();

            return crownsToSeed.Count;
        }

        private async Task<long?> GetCurrentPlaycountForUser(string artistName, string lastFmUserName, int userId)
        {
            var artist = await this._lastFmService.GetArtistInfoAsync(artistName, lastFmUserName);

            await this._globalUpdateService.UpdateUser(new UpdateUserQueueItem(userId, 0));

            if (!artist.Success || !artist.Content.Artist.Stats.Userplaycount.HasValue)
            {
                return null;
            }

            await this._globalUpdateService.CorrectUserArtistPlaycount(userId, artistName,
                artist.Content.Artist.Stats.Userplaycount.Value);

            return artist.Content.Artist.Stats.Userplaycount;
        }

        public async Task<IList<UserCrown>> GetCrownsForArtist(Persistence.Domain.Models.Guild guild, string artistName)
        {
            await using var db = this._contextFactory.CreateDbContext();
            return await db.UserCrowns
                .AsQueryable()
                .Include(i => i.User)
                .OrderByDescending(o => o.CurrentPlaycount)
                .Where(f => f.GuildId == guild.GuildId &&
                            f.ArtistName.ToLower() == artistName.ToLower())
                .ToListAsync();
        }

        public async Task RemoveCrowns(IList<UserCrown> crowns)
        {
            await using var db = this._contextFactory.CreateDbContext();

            db.UserCrowns.RemoveRange(crowns);

            await db.SaveChangesAsync();
        }

        public async Task<IList<UserCrown>> GetCrownsForUser(Persistence.Domain.Models.Guild guild, int userId)
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

        public async Task<List<IGrouping<int, UserCrown>>> GetTopCrownUsersForGuild(Persistence.Domain.Models.Guild guild)
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

        public async Task<int> GetTotalCrownCountForGuild(Persistence.Domain.Models.Guild guild)
        {
            await using var db = this._contextFactory.CreateDbContext();
            return await db.UserCrowns
                .AsQueryable()
                .Include(i => i.User)
                .Where(f => f.GuildId == guild.GuildId &&
                            f.Active)
                .CountAsync();
        }

        public async Task<IList<UserCrown>> GetAllCrownsForGuild(int guildId)
        {
            await using var db = this._contextFactory.CreateDbContext();
            return await db.UserCrowns
                .AsQueryable()
                .Include(i => i.User)
                .Where(w => w.GuildId == guildId)
                .ToListAsync();
        }

        public async Task RemoveAllCrownsFromGuild(Persistence.Domain.Models.Guild guild)
        {
            await using var db = this._contextFactory.CreateDbContext();
            var guildCrowns = await db.UserCrowns
                .AsQueryable()
                .Where(w => w.GuildId == guild.GuildId)
                .ToListAsync();

            db.UserCrowns.RemoveRange(guildCrowns);

            await db.SaveChangesAsync();
        }

        public async Task RemoveAllSeededCrownsFromGuild(Persistence.Domain.Models.Guild guild)
        {
            await using var db = this._contextFactory.CreateDbContext();
            var guildCrowns = await db.UserCrowns
                .AsQueryable()
                .Where(w => w.GuildId == guild.GuildId && w.SeededCrown)
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

            if (guild?.GuildCrowns != null && guild.GuildCrowns.Any())
            {
                var userGuildCrowns = await db.UserCrowns
                    .AsQueryable()
                    .Where(f => f.UserId == userThatLeft.UserId &&
                                f.GuildId == guild.GuildId)
                    .ToListAsync();

                if (userGuildCrowns != null && userGuildCrowns.Any())
                {
                    db.UserCrowns.RemoveRange(userGuildCrowns);

                    await db.SaveChangesAsync();
                }
            }
        }

    }
}
