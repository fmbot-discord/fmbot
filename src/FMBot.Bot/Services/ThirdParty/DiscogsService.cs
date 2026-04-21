using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FMBot.Discogs.Apis;
using FMBot.Discogs.Models;
using FMBot.Domain.Models;
using FMBot.Persistence.Domain.Models;
using FMBot.Persistence.EntityFrameWork;
using FMBot.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using Serilog;
using User = FMBot.Persistence.Domain.Models.User;

namespace FMBot.Bot.Services.ThirdParty;

public class DiscogsService
{
    private readonly DiscogsApi _discogsApi;
    private readonly IDbContextFactory<FMBotDbContext> _contextFactory;
    private readonly BotSettings _botSettings;

    public DiscogsService(IDbContextFactory<FMBotDbContext> contextFactory, DiscogsApi discogsApi, IOptions<BotSettings> botSettings)
    {
        this._contextFactory = contextFactory;
        this._discogsApi = discogsApi;
        this._botSettings = botSettings.Value;
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

        var uniqueReleases = releases.Releases
            .GroupBy(g => g.Id)
            .Select(g => g.First())
            .ToList();

        var ids = uniqueReleases.Select(s => s.Id);
        var existingReleases = await db.DiscogsReleases
            .Where(f => ids.Contains(f.DiscogsId))
            .ToListAsync();

        foreach (var release in uniqueReleases)
        {
            var existingRelease = existingReleases.FirstOrDefault(f => f.DiscogsId == release.Id);
            if (existingRelease == null)
            {
                var newRelease = new DiscogsRelease { DiscogsId = release.Id };
                PopulateRelease(newRelease, release.BasicInformation);

                await db.DiscogsReleases.AddAsync(newRelease);

                existingReleases.Add(newRelease);
            }
            else
            {
                PopulateRelease(existingRelease, release.BasicInformation);

                await db.Database.ExecuteSqlAsync($"""
                    DELETE FROM discogs_format_descriptions WHERE release_id = {existingRelease.DiscogsId};
                    DELETE FROM discogs_genre WHERE release_id = {existingRelease.DiscogsId};
                    DELETE FROM discogs_style WHERE release_id = {existingRelease.DiscogsId}
                    """);

                db.DiscogsReleases.Update(existingRelease);
            }
        }

        foreach (var release in releases.Releases)
        {
            await db.UserDiscogsReleases.AddAsync(new UserDiscogsReleases
            {
                DateAdded = DateTime.SpecifyKind(release.DateAdded, DateTimeKind.Utc),
                InstanceId = release.InstanceId,
                Quantity = release.BasicInformation.Formats.First().Qty,
                Rating = release.Rating == 0 ? null : release.Rating,
                UserId = user.UserId,
                ReleaseId = release.Id
            });
        }

        var artistNames = existingReleases
            .Select(r => r.Artist)
            .Concat(existingReleases.Where(r => r.FeaturingArtist != null).Select(r => r.FeaturingArtist))
            .Where(name => name != null)
            .Distinct()
            .ToList();

        var albumPairs = existingReleases
            .Where(r => r.Artist != null && r.Title != null)
            .Select(r => (r.Artist, r.Title))
            .Distinct()
            .ToList();

        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        var artistLookup = await ArtistRepository.GetArtistIdsForNames(artistNames, connection);
        var albumLookup = await AlbumRepository.GetAlbumIdsForNames(albumPairs, connection);

        foreach (var release in existingReleases)
        {
            release.ArtistId = release.Artist != null && artistLookup.TryGetValue(release.Artist, out var artistId)
                ? artistId
                : null;
            release.FeaturingArtistId = release.FeaturingArtist != null && artistLookup.TryGetValue(release.FeaturingArtist, out var featArtistId)
                ? featArtistId
                : null;
            release.AlbumId = release.Artist != null && release.Title != null
                ? albumLookup.GetValueOrDefault((release.Artist.ToLower(), release.Title.ToLower()))
                : null;
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
            try
            {
                Log.Information("Discogs: Automatically updating {userId}", user.UserId);
                await UpdateUserDiscogs(user);
            }
            catch (HttpRequestException e) when (e.StatusCode == HttpStatusCode.NotFound)
            {
                Log.Warning("Discogs: Removing Discogs link for user {userId} - account no longer exists", user.UserId);
                await RemoveDiscogs(user.UserId);
            }
            catch (Exception e)
            {
                Log.Error(e, "Discogs: Error while automatically updating {userId}", user.UserId);
            }

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
            .OrderBy(o => o.UserDiscogs.ReleasesLastUpdated)
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

    private static readonly Regex DiscogsDisambiguationRegex = new(@"\s\(\d+\)$", RegexOptions.Compiled);

    private static string StripDiscogsDisambiguation(string name) =>
        name != null ? DiscogsDisambiguationRegex.Replace(name, "") : null;

    private static void PopulateRelease(DiscogsRelease target, BasicInformation info)
    {
        target.MasterId = info.MasterId == 0 ? null : info.MasterId;
        target.CoverUrl = info.CoverImage;
        target.Format = info.Formats?.FirstOrDefault()?.Name;
        target.FormatText = info.Formats?.FirstOrDefault()?.Text;
        target.Label = info.Labels?.FirstOrDefault()?.Name;
        target.SecondLabel = info.Labels?.Count > 1 ? info.Labels[1].Name : null;
        target.Year = info.Year;
        target.Artist = StripDiscogsDisambiguation(info.Artists.First().Name);
        target.Title = info.Title;
        target.ArtistDiscogsId = info.Artists.First().Id;
        target.FeaturingArtistJoin = info.Artists.First().Join;
        target.FeaturingArtist = info.Artists.Count > 1 ? StripDiscogsDisambiguation(info.Artists[1].Name) : null;
        target.FeaturingArtistDiscogsId = info.Artists.Count > 1 ? info.Artists[1].Id : null;
        target.FormatDescriptions = info.Formats?.FirstOrDefault()?.Descriptions?.Select(s => new DiscogsFormatDescriptions
        {
            Description = s
        }).ToList();
        target.Genres = info.Genres?.Select(s => new DiscogsGenre
        {
            Description = s
        }).ToList();
        target.Styles = info.Styles?.Select(s => new DiscogsStyle
        {
            Description = s
        }).ToList();
    }
}
