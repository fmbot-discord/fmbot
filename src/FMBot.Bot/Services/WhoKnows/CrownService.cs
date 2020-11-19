using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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

        public async Task<string> GetAndUpdateCrownForArtist(IList<WhoKnowsObjectWithUser> users, Guild guild, string artistName)
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
                .OrderByDescending(o => o.Playcount)
                .First();

            await using var db = this._contextFactory.CreateDbContext();
            var existingCrownForArtist = await db.UserCrowns
                .AsQueryable()
                .Include(i => i.User)
                .Where(w => eligibleUsers.Select(s => s.UserId).Contains(w.UserId))
                .OrderByDescending(o => o.CurrentPlaycount)
                .FirstOrDefaultAsync(f => f.GuildId == guild.GuildId &&
                                          EF.Functions.ILike(f.ArtistName, artistName));

            // Crown exists and is same as top user
            if (existingCrownForArtist != null && topUser.UserId == existingCrownForArtist.UserId)
            {
                var oldPlaycount = existingCrownForArtist.CurrentPlaycount;
                if (oldPlaycount < topUser.Playcount)
                {
                    existingCrownForArtist.CurrentPlaycount = topUser.Playcount;
                    existingCrownForArtist.Modified = DateTime.UtcNow;

                    db.Entry(existingCrownForArtist).State = EntityState.Modified;
                    await db.SaveChangesAsync();

                    return $"Crown playcount for {topUser.DiscordName} updated from {oldPlaycount} to {topUser.Playcount}.";
                }

                return null;
            }

            // Crown exists, but top user is a different person
            if (existingCrownForArtist != null && topUser.UserId != existingCrownForArtist.UserId)
            {
                var currentPlaycountForCrownUser =
                    await GetCurrentPlaycountForUser(artistName, existingCrownForArtist.User.UserNameLastFM);

                if (currentPlaycountForCrownUser == null)
                {
                    return $"Could not confirm playcount for current crown holder.";
                }
                if (currentPlaycountForCrownUser >= topUser.Playcount)
                {
                    var oldPlaycount = existingCrownForArtist.CurrentPlaycount;
                    existingCrownForArtist.CurrentPlaycount = topUser.Playcount;
                    existingCrownForArtist.Modified = DateTime.UtcNow;

                    db.Entry(existingCrownForArtist).State = EntityState.Modified;

                    await db.SaveChangesAsync();

                    return $"Crown playcount for {topUser.DiscordName} updated from {oldPlaycount} to {topUser.Playcount}.";
                }

                if (existingCrownForArtist.CurrentPlaycount != currentPlaycountForCrownUser)
                {
                    existingCrownForArtist.CurrentPlaycount = topUser.Playcount;
                }

                existingCrownForArtist.Active = false;
                existingCrownForArtist.Modified = DateTime.UtcNow;

                db.Entry(existingCrownForArtist).State = EntityState.Modified;

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

                return $"Crown stolen by {topUser.DiscordName}! ({existingCrownForArtist.CurrentPlaycount} > {topUser.Playcount} plays).";
            }

            // No crown exists yet
            if (existingCrownForArtist == null)
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

                    return $"Crown claimed by {topUser.DiscordName}!";
                }

                if (topUser.Playcount >= 10)
                {
                    var amountOfPlaysRequired = Constants.MinPlaysForCrown - topUser.Playcount;
                    return $"{topUser.DiscordName} needs {amountOfPlaysRequired} more plays to claim the crown.";
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
    }
}
