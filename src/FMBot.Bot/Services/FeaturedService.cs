using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using FMBot.Bot.Extensions;
using FMBot.Domain;
using FMBot.Domain.Models;
using FMBot.LastFM.Repositories;
using FMBot.Persistence.Domain.Models;
using FMBot.Persistence.EntityFrameWork;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Serilog;

namespace FMBot.Bot.Services
{
    public class FeaturedService
    {
        private readonly LastFmRepository _lastFmRepository;
        private readonly IDbContextFactory<FMBotDbContext> _contextFactory;
        private readonly CensorService _censorService;
        private readonly UserService _userService;
        private readonly IMemoryCache _cache;

        public FeaturedService(LastFmRepository lastFmRepository,
            IDbContextFactory<FMBotDbContext> contextFactory,
            CensorService censorService,
            UserService userService, IMemoryCache cache)
        {
            this._lastFmRepository = lastFmRepository;
            this._contextFactory = contextFactory;
            this._censorService = censorService;
            this._userService = userService;
            this._cache = cache;
        }

        public async Task<FeaturedLog> NewFeatured(ulong botUserId, DateTime? dateTime = null)
        {
            var randomAvatarMode = RandomNumberGenerator.GetInt32(1, 4);

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

            var featuredLog = new FeaturedLog
            {
                FeaturedMode = featuredMode,
                BotType = BotTypeExtension.GetBotType(botUserId),
                DateTime = DateTime.SpecifyKind(dateTime ?? DateTime.UtcNow, DateTimeKind.Utc),
                HasFeatured = false
            };

            Log.Information($"Featured: Picked mode {randomAvatarMode} - {randomAvatarModeDesc}");

            try
            {
                var user = await GetUserToFeatureAsync();
                Log.Information($"Featured: Picked user {user.UserId} / {user.UserNameLastFM}");

                switch (featuredMode)
                {
                    // Recent listens
                    case FeaturedMode.RecentPlays:
                        var tracks = await this._lastFmRepository.GetRecentTracksAsync(user.UserNameLastFM, 50, sessionKey: user.SessionKeyLastFm);

                        if (!tracks.Success || tracks.Content?.RecentTracks == null)
                        {
                            Log.Information($"Featured: User {user.UserNameLastFM} had no recent tracks, switching to alternative avatar mode");
                            goto case FeaturedMode.TopAlbumsWeekly;
                        }

                        RecentTrack trackToFeature = null;
                        foreach (var track in tracks.Content.RecentTracks.Where(w => w.AlbumName != null && w.AlbumCoverUrl != null))
                        {
                            if (await this._censorService.AlbumIsSafe(track.AlbumName, track.ArtistName) &&
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

                        featuredLog.Description = $"[{trackToFeature.AlbumName}]({trackToFeature.TrackUrl}) \n" +
                                           $"by [{trackToFeature.ArtistName}]({trackToFeature.ArtistUrl}) \n\n" +
                                           $"{randomAvatarModeDesc} from {user.UserNameLastFM}";
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

                        var albums = await this._lastFmRepository.GetTopAlbumsAsync(user.UserNameLastFM, timespan, 30);

                        if (!albums.Success || albums.Content?.TopAlbums == null || !albums.Content.TopAlbums.Any())
                        {
                            Log.Information($"Featured: User {user.UserNameLastFM} had no albums, switching to different user.");
                            user = await GetUserToFeatureAsync();
                            albums = await this._lastFmRepository.GetTopAlbumsAsync(user.UserNameLastFM, timespan, 30);
                        }

                        var albumList = albums.Content.TopAlbums.ToList();

                        var albumFound = false;
                        var i = 0;

                        while (!albumFound)
                        {
                            var currentAlbum = albumList[i];

                            if (currentAlbum.AlbumCoverUrl != null &&
                                currentAlbum.AlbumName != null &&
                                await this._censorService.AlbumIsSafe(currentAlbum.AlbumName, currentAlbum.ArtistName) &&
                                await AlbumNotFeaturedRecently(currentAlbum.AlbumName, currentAlbum.ArtistName) &&
                                await AlbumPopularEnough(currentAlbum.AlbumName, currentAlbum.ArtistName))
                            {
                                featuredLog.Description = $"[{currentAlbum.AlbumName}]({currentAlbum.AlbumUrl}) \n" +
                                                          $"by {currentAlbum.ArtistName} \n\n" +
                                                          $"{randomAvatarModeDesc} from {user.UserNameLastFM}";
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
                .FirstOrDefaultAsync(w => w.FeaturedLogId == id);
        }

        public async Task<List<FeaturedLog>> GetFeaturedHistoryForUser(int id)
        {
            await using var db = await this._contextFactory.CreateDbContextAsync();

            return await db.FeaturedLogs
                .AsQueryable()
                .Where(w => w.UserId == id &&
                            w.HasFeatured)
                .ToListAsync();
        }

        private async Task<bool> AlbumPopularEnough(string albumName, string artistName)
        {
            var album = await this._lastFmRepository.GetAlbumInfoAsync(artistName, albumName);

            if (!album.Success || album.Content == null || album.Content.TotalListeners < 2500)
            {
                Log.Information("Featured: Album call failed or album not popular enough");
                return false;
            }

            return true;
        }

        private async Task<User> GetUserToFeatureAsync()
        {
            await using var db = await this._contextFactory.CreateDbContextAsync();
            var featuredUsers = await db.Users
                .AsQueryable()
                .Where(f => f.Featured == true)
                .ToListAsync();

            if (featuredUsers.Any())
            {
                foreach (var featuredUser in featuredUsers)
                {
                    featuredUser.Featured = false;
                    db.Entry(featuredUser).State = EntityState.Modified;
                }
            }

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

            var user = users[RandomNumberGenerator.GetInt32(0, users.Count)];

            user.Featured = true;

            db.Entry(user).State = EntityState.Modified;
            await db.SaveChangesAsync();

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

            var filterDate = DateTime.UtcNow.AddDays(-7);
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
                    await this._lastFmRepository.ScrobbleAsync(botUser, featuredLog.ArtistName, featuredLog.TrackName, featuredLog.AlbumName);
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

            var newFeatured = await NewFeatured(botUserId);

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
}
