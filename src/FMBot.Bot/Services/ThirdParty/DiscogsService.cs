using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FMBot.Discogs.Apis;
using FMBot.Discogs.Models;
using FMBot.Domain.Models;
using FMBot.Persistence.Domain.Models;
using FMBot.Persistence.EntityFrameWork;
using Microsoft.EntityFrameworkCore;
using Serilog;
using User = FMBot.Persistence.Domain.Models.User;

namespace FMBot.Bot.Services.ThirdParty;

public class DiscogsService
{
    private readonly DiscogsApi _discogsApi;
    private readonly IDbContextFactory<FMBotDbContext> _contextFactory;

    public DiscogsService(IDbContextFactory<FMBotDbContext> contextFactory, DiscogsApi discogsApi)
    {
        this._contextFactory = contextFactory;
        this._discogsApi = discogsApi;
    }

    public async Task<DiscogsAuthInitialization> GetDiscogsAuthLink()
    {
        return await this._discogsApi.GetDiscogsAuthLink();
    }

    public async Task<(DiscogsAuth Auth, DiscogsIdentity Identity)> ConfirmDiscogsAuth(int userId, DiscogsAuthInitialization discogsAuthInit, string verifier)
    {
        var auth = await this._discogsApi.StoreDiscogsAuth(discogsAuthInit, verifier);

        if (auth == null)
        {
            return (null, null);
        }

        return (auth, await this._discogsApi.GetIdentity(auth));
    }

    public async Task StoreDiscogsAuth(int userId, DiscogsAuth discogsAuth, DiscogsIdentity discogsIdentity)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        var user = await db.Users
            .Include(i => i.UserDiscogs)
            .FirstAsync(f => f.UserId == userId);

        if (user.UserDiscogs == null)
        {
            user.UserDiscogs = new UserDiscogs
            {
                AccessToken = discogsAuth.AccessToken,
                AccessTokenSecret = discogsAuth.AccessTokenSecret,
                DiscogsId = discogsIdentity.Id,
                Username = discogsIdentity.Username,
                LastUpdated = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc),
                UserId = user.UserId
            };

            db.Update(user);
        }
        else
        {
            user.UserDiscogs.AccessToken = discogsAuth.AccessToken;
            user.UserDiscogs.AccessTokenSecret = discogsAuth.AccessTokenSecret;
            user.UserDiscogs.DiscogsId = discogsIdentity.Id;
            user.UserDiscogs.Username = discogsIdentity.Username;
            user.UserDiscogs.LastUpdated = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc);

            db.Update(user.UserDiscogs);
        }

        await db.SaveChangesAsync();
    }

    public async Task<UserDiscogs> StoreUserReleases(User user)
    {
        var discogsAuth = new DiscogsAuth(user.UserDiscogs.AccessToken,
            user.UserDiscogs.AccessTokenSecret);

        var pages = SupporterService.IsSupporter(user.UserType) ? 50 : 1;

        var releases = await this._discogsApi.GetUserReleases(discogsAuth, user.UserDiscogs.Username, pages);

        if (releases?.Releases == null || releases.Releases.Count == 0)
        {
            user.DiscogsReleases = new List<UserDiscogsReleases>();

            return user.UserDiscogs;
        }

        await using var db = await this._contextFactory.CreateDbContextAsync();

        var ids = releases.Releases.Select(s => s.Id);
        var existingReleases = await db.DiscogsReleases
            .Where(f => ids.Contains(f.DiscogsId))
            .ToListAsync();

        foreach (var release in releases.Releases)
        {
            var userDiscogsRelease = new UserDiscogsReleases
            {
                DateAdded = DateTime.SpecifyKind(release.DateAdded, DateTimeKind.Utc),
                InstanceId = release.InstanceId,
                Quantity = release.BasicInformation.Formats.First().Qty,
                Rating = release.Rating == 0 ? null : release.Rating,
                UserId = user.UserId
            };

            var existingRelease = existingReleases.FirstOrDefault(f => f.DiscogsId == release.Id);
            if (existingRelease == null)
            {
                var newRelease = new DiscogsRelease
                {
                    DiscogsId = release.Id,
                    MasterId = release.BasicInformation.MasterId == 0 ? null : release.BasicInformation.MasterId,
                    CoverUrl = release.BasicInformation.CoverImage,
                    Format = release.BasicInformation.Formats?.FirstOrDefault()?.Name,
                    FormatText = release.BasicInformation.Formats?.FirstOrDefault()?.Text,
                    Label = release.BasicInformation.Labels?.FirstOrDefault()?.Name,
                    SecondLabel = release.BasicInformation.Labels?.Count > 1
                        ? release.BasicInformation.Labels[1].Name
                        : null,
                    Year = release.BasicInformation.Year,
                    FormatDescriptions = release.BasicInformation.Formats.FirstOrDefault()?.Descriptions?.Select(s => new DiscogsFormatDescriptions
                    {
                        Description = s
                    }).ToList(),
                    Artist = release.BasicInformation.Artists.First().Name,
                    Title = release.BasicInformation.Title,
                    ArtistDiscogsId = release.BasicInformation.Artists.First().Id,
                    FeaturingArtistJoin = release.BasicInformation.Artists.First().Join,
                    FeaturingArtist = release.BasicInformation.Artists.Count > 1
                        ? release.BasicInformation.Artists[1].Name
                        : null,
                    FeaturingArtistDiscogsId = release.BasicInformation.Artists.Count > 1
                        ? release.BasicInformation.Artists[1].Id
                        : null,
                    Genres = release.BasicInformation.Genres?.Select(s => new DiscogsGenre
                    {
                        Description = s
                    }).ToList(),
                    Styles = release.BasicInformation.Styles?.Select(s => new DiscogsStyle
                    {
                        Description = s
                    }).ToList()
                };

                await db.DiscogsReleases.AddAsync(newRelease);

                existingReleases.Add(newRelease);

                userDiscogsRelease.ReleaseId = newRelease.DiscogsId;
            }
            else
            {
                userDiscogsRelease.ReleaseId = existingRelease.DiscogsId;
            }

            await db.UserDiscogsReleases.AddAsync(userDiscogsRelease);
        }

        await db.Database.ExecuteSqlAsync($"DELETE FROM user_discogs_releases WHERE user_id = {user.UserId}");

        await db.SaveChangesAsync();

        return user.UserDiscogs;
    }

    public async Task<UserDiscogs> UpdateCollectionValue(int userId)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();

        var user = await db.Users
            .Include(i => i.UserDiscogs)
            .FirstAsync(f => f.UserId == userId);

        user.UserDiscogs.ReleasesLastUpdated = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc);

        var collectionValue = await this._discogsApi.GetCollectionValue(
            new DiscogsAuth(user.UserDiscogs.AccessToken, user.UserDiscogs.AccessTokenSecret),
            user.UserDiscogs.Username);

        if (collectionValue != null)
        {
            user.UserDiscogs.MinimumValue = collectionValue.Minimum;
            user.UserDiscogs.MedianValue = collectionValue.Median;
            user.UserDiscogs.MaximumValue = collectionValue.Maximum;
        }

        db.UserDiscogs.Update(user.UserDiscogs);

        await db.SaveChangesAsync();

        return user.UserDiscogs;
    }

    public async Task<bool?> ToggleCollectionValueHidden(int userId)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();

        var user = await db.Users
            .Include(i => i.UserDiscogs)
            .FirstAsync(f => f.UserId == userId);

        user.UserDiscogs.HideValue = user.UserDiscogs.HideValue != true;

        db.UserDiscogs.Update(user.UserDiscogs);
        await db.SaveChangesAsync();

        return user.UserDiscogs.HideValue;
    }

    public async Task RemoveDiscogs(int userId)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();

        var user = await db.Users
            .Include(i => i.UserDiscogs)
            .Include(i => i.DiscogsReleases)
            .FirstAsync(f => f.UserId == userId);

        db.UserDiscogs.Remove(user.UserDiscogs);

        if (user.DiscogsReleases.Count != 0)
        {
            db.UserDiscogsReleases.RemoveRange(user.DiscogsReleases);
        }

        await db.SaveChangesAsync();
    }

    public async Task UpdateDiscogsUsers(List<User> usersToUpdate)
    {
        foreach (var user in usersToUpdate)
        {
            Log.Information("Discogs: Automatically updating {userId}", user.UserId);
            await UpdateUserDiscogs(user);

            await Task.Delay(5000);
        }
    }

    public async Task<List<User>> GetOutdatedDiscogsUsers()
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();

        var updateCutoff = DateTime.UtcNow.AddMonths(-1);
        return await db.Users
            .Include(i => i.UserDiscogs)
            .Where(w => w.UserDiscogs != null &&
                        w.UserDiscogs.ReleasesLastUpdated < updateCutoff)
            .Take(500)
            .ToListAsync();
    }

    private async Task UpdateUserDiscogs(User user)
    {
        user.UserDiscogs = await this.StoreUserReleases(user);
        user.UserDiscogs = await this.UpdateCollectionValue(user.UserId);
    }

    public async Task<List<UserDiscogsReleases>> GetUserCollection(int userId)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        return await db.UserDiscogsReleases
            .Include(i => i.Release)
            .ThenInclude(i => i.FormatDescriptions)
            .Where(w => w.UserId == userId)
            .ToListAsync();
    }

    public static int? DiscogsReleaseUrlToId(string url)
    {
        var identifier = url
            .Replace("https://www.discogs.com/release/", "", StringComparison.OrdinalIgnoreCase)
            .Replace("<", "")
            .Replace(">", "");

        var splitIdentifier = identifier.Split('-');
        var id = splitIdentifier[0];

        if (int.TryParse(id, out var result))
        {
            return result;
        }

        return null;
    }

    public async Task<DiscogsFullRelease> GetDiscogsRelease(int userId, int releaseId)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();

        var user = await db.Users
            .Include(i => i.UserDiscogs)
            .FirstAsync(f => f.UserId == userId);

        return await this._discogsApi.GetRelease(
            new DiscogsAuth(user.UserDiscogs.AccessToken, user.UserDiscogs.AccessTokenSecret),
            releaseId);
    }
}
