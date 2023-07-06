using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Discord;
using FMBot.Bot.Extensions;
using FMBot.Bot.Models;
using FMBot.Domain;
using FMBot.Domain.Interfaces;
using FMBot.Domain.Models;
using FMBot.LastFM.Extensions;
using FMBot.LastFM.Repositories;
using FMBot.Persistence.Domain.Models;
using FMBot.Persistence.EntityFrameWork;
using Google.Apis.Discovery;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Serilog;

namespace FMBot.Bot.Services;

public class FeaturedService
{
    private readonly IDataSourceFactory _dataSourceFactory;
    private readonly IDbContextFactory<FMBotDbContext> _contextFactory;
    private readonly CensorService _censorService;
    private readonly UserService _userService;
    private readonly IMemoryCache _cache;

    public FeaturedService(IDataSourceFactory dataSourceFactory,
        IDbContextFactory<FMBotDbContext> contextFactory,
        CensorService censorService,
        UserService userService, IMemoryCache cache)
    {
        this._dataSourceFactory = dataSourceFactory;
        this._contextFactory = contextFactory;
        this._censorService = censorService;
        this._userService = userService;
        this._cache = cache;
    }

    public async Task<FeaturedLog> NewFeatured(DateTime? dateTime = null)
    {
        var randomAvatarMode = RandomNumberGenerator.GetInt32(1, 5);

        if (!Enum.TryParse(randomAvatarMode.ToString(), out FeaturedMode featuredMode))
        {
            return null;
        }

        var randomAvatarModeDesc = "";

        switch (featuredMode)
        {
            case FeaturedMode.RecentPlays:
                randomAvatarModeDesc = "Recent listens";
                break;
            case FeaturedMode.TopAlbumsWeekly:
                randomAvatarModeDesc = "Weekly albums";
                break;
            case FeaturedMode.TopAlbumsMonthly:
                randomAvatarModeDesc = "Monthly albums";
                break;
            case FeaturedMode.TopAlbumsAllTime:
                randomAvatarModeDesc = "Overall albums";
                break;
        }

        var supporterDay = dateTime is { DayOfWeek: DayOfWeek.Sunday, Day: <= 7 };

        var featuredLog = new FeaturedLog
        {
            FeaturedMode = featuredMode,
            BotType = BotType.Production,
            DateTime = DateTime.SpecifyKind(dateTime ?? DateTime.UtcNow, DateTimeKind.Utc),
            HasFeatured = false,
            SupporterDay = supporterDay
        };

        Log.Information($"Featured: Picked mode {randomAvatarMode} - {randomAvatarModeDesc}");

        try
        {
            var user = await GetUserToFeatureAsync(Constants.DaysLastUsedForFeatured + (supporterDay ? 6 : 0), supporterDay);

            if (user == null)
            {
                return null;
            }

            Log.Information($"Featured: Picked user {user.UserId} / {user.UserNameLastFM}");

            switch (featuredMode)
            {
                // Recent listens
                case FeaturedMode.RecentPlays:
                    var tracks = await this._dataSourceFactory.GetRecentTracksAsync(user.UserNameLastFM, 50, sessionKey: user.SessionKeyLastFm);

                    if (!tracks.Success || tracks.Content?.RecentTracks == null)
                    {
                        Log.Information($"Featured: User {user.UserNameLastFM} had no recent tracks, switching to alternative avatar mode");
                        goto case FeaturedMode.TopAlbumsWeekly;
                    }

                    RecentTrack trackToFeature = null;
                    foreach (var track in tracks.Content.RecentTracks.Where(w => w.AlbumName != null && w.AlbumCoverUrl != null))
                    {
                        if (await this._censorService.AlbumResult(track.AlbumName, track.ArtistName, true) == CensorService.CensorResult.Safe &&
                            await AlbumNotFeaturedRecently(track.AlbumName, track.ArtistName) &&
                            await AlbumPopularEnough(track.AlbumName, track.ArtistName))
                        {
                            trackToFeature = track;
                            break;
                        }

                        await Task.Delay(400);
                    }

                    if (trackToFeature == null)
                    {
                        Log.Information("Featured: No albums or nsfw filtered, switching to alternative avatar mode");
                        goto case FeaturedMode.TopAlbumsMonthly;
                    }

                    if (supporterDay)
                    {
                        featuredLog.Description = $"[{trackToFeature.AlbumName}]({trackToFeature.TrackUrl}) \n" +
                                                  $"by [{trackToFeature.ArtistName}]({trackToFeature.ArtistUrl}) \n\n" +
                                                  $"{randomAvatarModeDesc}\n" +
                                                  $"⭐ Supporter Sunday - Thanks {user.UserNameLastFM} for supporting .fmbot!";
                    }
                    else
                    {
                        featuredLog.Description = $"[{trackToFeature.AlbumName}]({trackToFeature.TrackUrl}) \n" +
                                                  $"by [{trackToFeature.ArtistName}]({trackToFeature.ArtistUrl}) \n\n" +
                                                  $"{randomAvatarModeDesc} from {user.UserNameLastFM}";
                    }

                    featuredLog.UserId = user.UserId;

                    featuredLog.ArtistName = trackToFeature.ArtistName;
                    featuredLog.TrackName = trackToFeature.TrackName;
                    featuredLog.AlbumName = trackToFeature.AlbumName;
                    featuredLog.ImageUrl = trackToFeature.AlbumCoverUrl;

                    return featuredLog;
                case FeaturedMode.TopAlbumsWeekly:
                case FeaturedMode.TopAlbumsMonthly:
                case FeaturedMode.TopAlbumsAllTime:
                    var timespan = TimePeriod.Weekly;
                    switch (featuredMode)
                    {
                        case FeaturedMode.TopAlbumsMonthly:
                            timespan = TimePeriod.Monthly;
                            break;
                        case FeaturedMode.TopAlbumsAllTime:
                            timespan = TimePeriod.AllTime;
                            break;
                    }

                    var albums = await this._dataSourceFactory.GetTopAlbumsAsync(user.UserNameLastFM, timespan, 50);

                    if (!albums.Success || albums.Content?.TopAlbums == null || !albums.Content.TopAlbums.Any())
                    {
                        Log.Information($"Featured: User {user.UserNameLastFM} had no albums, switching to different user.");
                        user = await GetUserToFeatureAsync(Constants.DaysLastUsedForFeatured + (supporterDay ? 6 : 0), supporterDay);
                        albums = await this._dataSourceFactory.GetTopAlbumsAsync(user.UserNameLastFM, timespan, 50);
                    }

                    var albumList = albums.Content.TopAlbums.ToList();

                    var albumFound = false;
                    var i = 0;

                    while (!albumFound)
                    {
                        var currentAlbum = albumList[i];

                        if (currentAlbum.AlbumCoverUrl != null &&
                            currentAlbum.AlbumName != null &&
                            await this._censorService.AlbumResult(currentAlbum.AlbumName, currentAlbum.ArtistName, true) == CensorService.CensorResult.Safe &&
                            await AlbumNotFeaturedRecently(currentAlbum.AlbumName, currentAlbum.ArtistName) &&
                            await AlbumPopularEnough(currentAlbum.AlbumName, currentAlbum.ArtistName))
                        {
                            var artistLink = LastfmUrlExtensions.GetArtistUrl(currentAlbum.ArtistName);
                            if (supporterDay)
                            {
                                featuredLog.Description = $"[{currentAlbum.AlbumName}]({currentAlbum.AlbumUrl}) \n" +
                                                          $"by [{currentAlbum.ArtistName}]({artistLink}) \n\n" +
                                                          $"{randomAvatarModeDesc}\n" +
                                                          $"⭐ Supporter Sunday - Thanks {user.UserNameLastFM} for supporting .fmbot!";
                            }
                            else
                            {
                                featuredLog.Description = $"[{currentAlbum.AlbumName}]({currentAlbum.AlbumUrl}) \n" +
                                                          $"by [{currentAlbum.ArtistName}]({artistLink}) \n\n" +
                                                          $"{randomAvatarModeDesc} from {user.UserNameLastFM}";
                            }

                            featuredLog.UserId = user.UserId;

                            featuredLog.AlbumName = currentAlbum.AlbumName;
                            featuredLog.ImageUrl = currentAlbum.AlbumCoverUrl;
                            featuredLog.ArtistName = currentAlbum.ArtistName;

                            albumFound = true;
                        }
                        else
                        {
                            i++;
                            await Task.Delay(400);
                        }
                    }

                    return featuredLog;
                default:
                    break;
            }
        }
        catch (Exception e)
        {
            Log.Error(e, "Error in GetFeatured");
        }

        return null;
    }

    public static int GetDaysUntilNextSupporterSunday()
    {
        var nextSupporterSunday = DateTime.UtcNow;
        while (nextSupporterSunday.DayOfWeek != DayOfWeek.Sunday || nextSupporterSunday.Day > 7)
        {
            nextSupporterSunday = nextSupporterSunday.AddDays(1);
        }

        var diff = nextSupporterSunday - DateTime.UtcNow;
        return (int)diff.TotalDays;
    }

    public async Task<FeaturedLog> GetFeaturedForDateTime(DateTime dateTime)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();

        var date = new DateTime(dateTime.Year, dateTime.Month, dateTime.Day, dateTime.Hour, Constants.FeaturedMinute, 0, kind: DateTimeKind.Utc);
        return await db.FeaturedLogs
            .AsQueryable()
            .FirstOrDefaultAsync(w => w.DateTime == date);
    }

    public async Task<FeaturedLog> GetFeaturedForId(int id)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();

        return await db.FeaturedLogs
            .AsQueryable()
            .Include(i => i.User)
            .FirstOrDefaultAsync(w => w.FeaturedLogId == id);
    }

    public async Task<List<FeaturedLog>> GetGlobalFeaturedHistory()
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();

        return await db.FeaturedLogs
            .AsQueryable()
            .Include(i => i.User)
            .Where(w => w.HasFeatured)
            .OrderByDescending(o => o.DateTime)
            .Take(240)
            .ToListAsync();
    }

    public async Task<List<FeaturedLog>> GetFeaturedHistoryForGuild(IDictionary<int, FullGuildUser> users)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();

        var friendIds = users
            .Select(s => s.Key)
            .ToList();

        return await db.FeaturedLogs
            .AsQueryable()
            .Include(i => i.User)
            .Where(w =>
                w.UserId != null &&
                friendIds.Contains(w.UserId.Value) &&
                w.HasFeatured)
            .OrderByDescending(o => o.DateTime)
            .ToListAsync();
    }

    public string GetStringForFeatured(FeaturedLog featured, bool displayUser = true, FullGuildUser user = null)
    {
        var description = new StringBuilder();

        if (featured.AlbumName != null)
        {
            description.Append(
                $"**[{featured.AlbumName}]({LastfmUrlExtensions.GetAlbumUrl(featured.ArtistName, featured.AlbumName)})** by " +
                $"**[{featured.ArtistName}]({LastfmUrlExtensions.GetArtistUrl(featured.ArtistName)})**");
        }
        else if (featured.ArtistName != null)
        {
            description.Append(
                $"**[{featured.ArtistName}]({LastfmUrlExtensions.GetArtistUrl(featured.ArtistName)})**");
        }
        else
        {
            description.Append(
                $"No artist or album name set.");
        }

        description.AppendLine();

        description.Append(GetStringForFeaturedMode(featured.FeaturedMode));

        if (displayUser)
        {
            if (user != null)
            {
                description.Append($" from **[{StringExtensions.Sanitize(user.UserName)}]({LastfmUrlExtensions.GetUserUrl(user.UserNameLastFM)})**");
            }
            else if (featured.User != null)
            {
                description.Append($" from **[{featured.User.UserNameLastFM}]({LastfmUrlExtensions.GetUserUrl(featured.User.UserNameLastFM)})**");
            }
        }

        if (featured.SupporterDay)
        {
            description.Append(" - Supporter Sunday ⭐");
        }

        description.AppendLine();

        var dateValue = ((DateTimeOffset)featured.DateTime).ToUnixTimeSeconds();
        description.Append($"<t:{dateValue}:f> - <t:{dateValue}:R>");

        return description.ToString();
    }

    public string GetStringForFeaturedMode(FeaturedMode featuredMode)
    {
        return featuredMode switch
        {
            FeaturedMode.Custom => "Custom",
            FeaturedMode.RecentPlays => "Recent listens",
            FeaturedMode.TopAlbumsWeekly => "Weekly albums",
            FeaturedMode.TopAlbumsMonthly => "Monthly albums",
            FeaturedMode.TopAlbumsAllTime => "Overall albums",
            _ => throw new ArgumentOutOfRangeException()
        };
    }

    public async Task<List<FeaturedLog>> GetFeaturedHistoryForFriends(int userId)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();

        var friends = await db.Friends
            .Where(w => w.UserId == userId && w.FriendUserId != null)
            .ToListAsync();

        var friendIds = friends
            .Select(s => s.FriendUserId)
            .ToList();

        return await db.FeaturedLogs
            .AsQueryable()
            .Include(i => i.User)
            .Where(w =>
                w.UserId != null &&
                friendIds.Contains(w.UserId) &&
                w.HasFeatured)
            .OrderByDescending(o => o.DateTime)
            .ToListAsync();
    }

    public async Task<List<FeaturedLog>> GetFeaturedHistoryForUser(int userId)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();

        return await db.FeaturedLogs
            .AsQueryable()
            .Include(i => i.User)
            .Where(w => w.UserId == userId &&
                        w.HasFeatured)
            .OrderByDescending(o => o.DateTime)
            .ToListAsync();
    }

    private async Task<bool> AlbumPopularEnough(string albumName, string artistName)
    {
        var album = await this._dataSourceFactory.GetAlbumInfoAsync(artistName, albumName);

        if (!album.Success || album.Content == null || album.Content.TotalListeners < 2500)
        {
            Log.Information("Featured: Album call failed or album not popular enough");
            return false;
        }

        return true;
    }

    private async Task<User> GetUserToFeatureAsync(int lastUsedFilter, bool supportersOnly = false)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        var lastFmUsersToFilter = await db.BottedUsers
            .AsQueryable()
            .Where(w => w.BanActive)
            .Select(s => s.UserNameLastFM.ToLower()).ToListAsync();

        var recentlyFeaturedFilter = supportersOnly ? DateTime.UtcNow.AddDays(-45) : DateTime.UtcNow.AddDays(-1);
        var recentlyFeaturedUsers = await db.FeaturedLogs
            .Include(i => i.User)
            .Where(w => w.DateTime > recentlyFeaturedFilter && w.UserId != null)
            .Select(s => s.User.UserNameLastFM.ToLower())
            .ToListAsync();

        if (recentlyFeaturedUsers.Any())
        {
            lastFmUsersToFilter.AddRange(recentlyFeaturedUsers);
        }

        var filterDate = DateTime.UtcNow.AddDays(-lastUsedFilter);
        var users = db.Users
            .AsQueryable()
            .Where(w => w.Blocked != true &&
                        !lastFmUsersToFilter.Contains(w.UserNameLastFM.ToLower()) &&
                        (!supportersOnly || w.UserType == UserType.Supporter) &&
                        w.LastUsed != null &&
                        w.LastUsed > filterDate).ToList();

        // Great coding for staff that also has supporter
        if (supportersOnly)
        {
            var voaz = await db.Users.FirstOrDefaultAsync(f => f.DiscordUserId == 119517941820686338);
            if (voaz != null)
            {
                users.Add(voaz);
            }
            var rndl = await db.Users.FirstOrDefaultAsync(f => f.DiscordUserId == 546055787835949077);
            if (rndl != null)
            {
                users.Add(rndl);
            }
            var drasil = await db.Users.FirstOrDefaultAsync(f => f.DiscordUserId == 278633844763262976);
            if (drasil != null)
            {
                users.Add(drasil);
            }
            var aeth = await db.Users.FirstOrDefaultAsync(f => f.DiscordUserId == 616906331537932300);
            if (aeth != null)
            {
                users.Add(aeth);
            }
        }

        if (users.Count == 0)
        {
            Log.Warning("No users eligible to be featured");
            return null;
        }

        var user = users[RandomNumberGenerator.GetInt32(0, users.Count)];

        return user;
    }

    public async Task<int> GetFeaturedOddsAsync()
    {
        const string cacheKey = "featured-odds";
        var cacheTime = TimeSpan.FromHours(1);

        if (this._cache.TryGetValue(cacheKey, out int odds))
        {
            return odds;
        }

        await using var db = await this._contextFactory.CreateDbContextAsync();
        var lastFmUsersToFilter = await db.BottedUsers
            .AsQueryable()
            .Where(w => w.BanActive)
            .Select(s => s.UserNameLastFM.ToLower()).ToListAsync();

        var filterDate = DateTime.UtcNow.AddDays(-Constants.DaysLastUsedForFeatured);
        var users = db.Users
            .AsQueryable()
            .Where(w => w.Blocked != true &&
                        !lastFmUsersToFilter.Contains(w.UserNameLastFM.ToLower()) &&
                        w.LastUsed != null &&
                        w.LastUsed > filterDate).ToList();

        this._cache.Set(cacheKey, users.Count, cacheTime);

        return users.Count;
    }

    private async Task<bool> AlbumNotFeaturedRecently(string albumName, string artistName)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();

        var filterDate = DateTime.UtcNow.AddDays(-28);
        var recentlyFeaturedAlbums = await db.FeaturedLogs
            .AsQueryable()
            .Where(w => w.DateTime > filterDate)
            .ToListAsync();

        if (!recentlyFeaturedAlbums.Any())
        {
            return true;
        }

        if (recentlyFeaturedAlbums.Where(w => w.AlbumName != null && w.ArtistName != null)
            .Select(s => $"{s.ArtistName.ToLower()}{s.AlbumName.ToLower()}")
            .Contains($"{artistName.ToLower()}{albumName.ToLower()}"))
        {
            Log.Information("Featured: Album featured recently");
            return false;
        }

        return true;
    }

    public async Task ScrobbleTrack(ulong botUserId, FeaturedLog featuredLog)
    {
        try
        {
            var botUser = await this._userService.GetUserAsync(botUserId);

            if (botUser?.SessionKeyLastFm != null)
            {
                await this._dataSourceFactory.ScrobbleAsync(botUser.SessionKeyLastFm, featuredLog.ArtistName, featuredLog.TrackName, featuredLog.AlbumName);
            }
        }
        catch (Exception exception)
        {
            Log.Error(exception, nameof(ScrobbleTrack));
        }
    }

    public async Task AddFeatured(FeaturedLog featuredLog)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();

        await db.FeaturedLogs.AddAsync(featuredLog);

        await db.SaveChangesAsync();
    }

    public async Task SetFeatured(FeaturedLog featuredLog)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();

        featuredLog.HasFeatured = true;
        db.Entry(featuredLog).State = EntityState.Modified;

        await db.SaveChangesAsync();
    }

    public async Task<FeaturedLog> ReplaceFeatured(FeaturedLog featuredLog, ulong botUserId)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();

        var newFeatured = await NewFeatured(featuredLog.DateTime);

        featuredLog.HasFeatured = false;
        featuredLog.AlbumName = newFeatured.AlbumName;
        featuredLog.TrackName = newFeatured.TrackName;
        featuredLog.ArtistName = newFeatured.ArtistName;
        featuredLog.UserId = newFeatured.UserId;
        featuredLog.Description = newFeatured.Description;
        featuredLog.ImageUrl = newFeatured.ImageUrl;
        db.Entry(featuredLog).State = EntityState.Modified;

        await db.SaveChangesAsync();

        return featuredLog;
    }

    public async Task<FeaturedLog> CustomFeatured(FeaturedLog featuredLog, string description, string imageUrl)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();

        featuredLog.HasFeatured = false;
        featuredLog.Description = description;
        featuredLog.ImageUrl = imageUrl;
        featuredLog.FeaturedMode = FeaturedMode.Custom;

        featuredLog.TrackName = null;
        featuredLog.AlbumName = null;
        featuredLog.ArtistName = null;
        featuredLog.UserId = null;

        db.Entry(featuredLog).State = EntityState.Modified;

        await db.SaveChangesAsync();

        return featuredLog;
    }
}
