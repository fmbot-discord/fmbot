using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Dapper;
using Discord;
using FMBot.Bot.Extensions;
using FMBot.Bot.Interfaces;
using FMBot.Bot.Models;
using FMBot.Bot.Services.WhoKnows;
using FMBot.Domain;
using FMBot.Domain.Enums;
using FMBot.Domain.Interfaces;
using FMBot.Domain.Models;
using FMBot.Domain.Types;
using FMBot.LastFM.Repositories;
using FMBot.Persistence.Domain.Models;
using FMBot.Persistence.EntityFrameWork;
using FMBot.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Npgsql;
using Serilog;

namespace FMBot.Bot.Services;

public class ArtistsService
{
    private readonly IDbContextFactory<FMBotDbContext> _contextFactory;
    private readonly IMemoryCache _cache;
    private readonly BotSettings _botSettings;
    private readonly ArtistRepository _artistRepository;
    private readonly IDataSourceFactory _dataSourceFactory;
    private readonly WhoKnowsArtistService _whoKnowsArtistService;
    private readonly TimerService _timer;
    private readonly IUpdateService _updateService;

    public ArtistsService(IDbContextFactory<FMBotDbContext> contextFactory,
        IMemoryCache cache,
        IOptions<BotSettings> botSettings,
        ArtistRepository artistRepository,
        IDataSourceFactory dataSourceFactory,
        WhoKnowsArtistService whoKnowsArtistService,
        TimerService timer,
        IUpdateService updateService)
    {
        this._contextFactory = contextFactory;
        this._cache = cache;
        this._artistRepository = artistRepository;
        this._dataSourceFactory = dataSourceFactory;
        this._whoKnowsArtistService = whoKnowsArtistService;
        this._timer = timer;
        this._updateService = updateService;
        this._botSettings = botSettings.Value;
    }

    public async Task<ArtistSearch> SearchArtist(ResponseModel response, IUser discordUser, string artistValues, string lastFmUserName, string sessionKey = null, string otherUserUsername = null,
        bool useCachedArtists = false, int? userId = null, bool redirectsEnabled = true)
    {
        if (!string.IsNullOrWhiteSpace(artistValues) && artistValues.Length != 0)
        {
            if (otherUserUsername != null)
            {
                lastFmUserName = otherUserUsername;
            }

            if (artistValues.ToLower() == "featured")
            {
                artistValues = this._timer.CurrentFeatured.ArtistName;
            }

            int? rndPosition = null;
            long? rndPlaycount = null;
            if (userId.HasValue && (artistValues.ToLower() == "rnd" || artistValues.ToLower() == "random"))
            {
                var topArtists = await this.GetUserAllTimeTopArtists(userId.Value, true);
                if (topArtists.Count > 0)
                {
                    var rnd = RandomNumberGenerator.GetInt32(0, topArtists.Count);

                    var artist = topArtists[rnd];

                    rndPosition = rnd;
                    rndPlaycount = artist.UserPlaycount;
                    artistValues = artist.ArtistName;
                }
            }

            Response<ArtistInfo> artistCall;
            if (useCachedArtists)
            {
                artistCall = await GetCachedArtist(artistValues, lastFmUserName, userId, redirectsEnabled);
            }
            else
            {
                artistCall = await this._dataSourceFactory.GetArtistInfoAsync(artistValues, lastFmUserName, redirectsEnabled);
            }

            if (!artistCall.Success && artistCall.Error == ResponseStatus.MissingParameters)
            {
                response.Embed.WithDescription($"Artist `{artistValues}` could not be found, please check your search values and try again.");
                response.CommandResponse = CommandResponse.NotFound;
                response.ResponseType = ResponseType.Embed;
                return new ArtistSearch(null, response);
            }
            if (!artistCall.Success || artistCall.Content == null)
            {
                response.Embed.ErrorResponse(artistCall.Error, artistCall.Message, null, discordUser, "artist");
                response.CommandResponse = CommandResponse.LastFmError;
                response.ResponseType = ResponseType.Embed;
                return new ArtistSearch(null, response);
            }

            return new ArtistSearch(artistCall.Content, response, rndPosition, rndPlaycount);
        }
        else
        {
            Response<RecentTrackList> recentScrobbles;

            if (userId.HasValue && otherUserUsername == null)
            {
                recentScrobbles = await this._updateService.UpdateUser(new UpdateUserQueueItem(userId.Value, getAccurateTotalPlaycount: false));
            }
            else
            {
                recentScrobbles = await this._dataSourceFactory.GetRecentTracksAsync(lastFmUserName, 1, true, sessionKey);
            }

            if (GenericEmbedService.RecentScrobbleCallFailed(recentScrobbles))
            {
                response.Embed = GenericEmbedService.RecentScrobbleCallFailedBuilder(recentScrobbles, lastFmUserName);
                response.ResponseType = ResponseType.Embed;
                return new ArtistSearch(null, response);
            }

            if (otherUserUsername != null)
            {
                lastFmUserName = otherUserUsername;
            }

            var lastPlayedTrack = recentScrobbles.Content.RecentTracks[0];
            Response<ArtistInfo> artistCall;
            if (useCachedArtists)
            {
                artistCall = await GetCachedArtist(lastPlayedTrack.ArtistName, lastFmUserName, userId, redirectsEnabled);
            }
            else
            {
                artistCall = await this._dataSourceFactory.GetArtistInfoAsync(lastPlayedTrack.ArtistName, lastFmUserName, redirectsEnabled);
            }

            if (artistCall.Content == null || !artistCall.Success)
            {
                response.Embed.WithDescription($"Last.fm did not return a result for **{lastPlayedTrack.ArtistName}**.");
                response.CommandResponse = CommandResponse.NotFound;
                response.ResponseType = ResponseType.Embed;
                return new ArtistSearch(null, response);
            }

            return new ArtistSearch(artistCall.Content, response);
        }
    }

    private async Task<Response<ArtistInfo>> GetCachedArtist(string artistName, string lastFmUserName, int? userId = null, bool redirectsEnabled = true)
    {
        Response<ArtistInfo> artistInfo;
        var cachedArtist = await GetArtistFromDatabase(artistName, redirectsEnabled);
        if (cachedArtist != null)
        {
            artistInfo = new Response<ArtistInfo>
            {
                Content = CachedArtistToArtistInfo(cachedArtist),
                Success = true
            };

            if (userId.HasValue)
            {
                var userPlaycount = await this._whoKnowsArtistService.GetArtistPlayCountForUser(cachedArtist.Name, userId.Value);
                artistInfo.Content.UserPlaycount = userPlaycount;
            }
        }
        else
        {
            artistInfo = await this._dataSourceFactory.GetArtistInfoAsync(artistName, lastFmUserName, redirectsEnabled);
        }

        return artistInfo;
    }

    private ArtistInfo CachedArtistToArtistInfo(Artist artist)
    {
        return new ArtistInfo
        {
            ArtistName = artist.Name,
            ArtistUrl = artist.LastFmUrl,
            Mbid = artist.Mbid
        };
    }

    public async Task<List<TopArtist>> FillArtistImages(List<TopArtist> topArtists)
    {
        if (topArtists.All(a => a.ArtistImageUrl != null))
        {
            return topArtists;
        }

        await CacheSpotifyArtistImages();

        foreach (var topArtist in topArtists.Where(w => w.ArtistImageUrl == null))
        {
            var artistImage = (string)this._cache.Get(CacheKeyForArtist(topArtist.ArtistName));

            if (artistImage != null)
            {
                topArtist.ArtistImageUrl = artistImage;
            }
        }

        return topArtists;
    }

    private async Task CacheSpotifyArtistImages()
    {
        const string cacheKey = "artist-spotify-covers";
        var cacheTime = TimeSpan.FromMinutes(5);

        if (this._cache.TryGetValue(cacheKey, out _))
        {
            return;
        }

        const string sql = "SELECT LOWER(spotify_image_url) as spotify_image_url, LOWER(name) as artist_name " +
                           "FROM public.artists where last_fm_url is not null and spotify_image_url is not null;";

        DefaultTypeMap.MatchNamesWithUnderscores = true;
        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        var artistCovers = (await connection.QueryAsync<ArtistSpotifyCoverDto>(sql)).ToList();

        foreach (var artistCover in artistCovers)
        {
            this._cache.Set(CacheKeyForArtist(artistCover.ArtistName), artistCover.SpotifyImageUrl, cacheTime);
        }

        this._cache.Set(cacheKey, true, cacheTime);
    }

    public static string CacheKeyForArtist(string artistName)
    {
        return $"artist-spotify-image-{artistName.ToLower()}";
    }

    // Top artists for 2 users
    public TasteModels GetEmbedTaste(ICollection<TasteItem> leftUserArtists,
        ICollection<TasteItem> rightUserArtists, int amount, TimePeriod timePeriod)
    {
        var matchedArtists = ArtistsToShow(leftUserArtists, rightUserArtists);

        var left = "";
        var right = "";
        foreach (var artist in matchedArtists.Take(amount))
        {
            var name = artist.Name;
            if (!string.IsNullOrWhiteSpace(name) && name.Length > 24)
            {
                left += $"**{name.Substring(0, 24)}..**\n";
            }
            else
            {
                left += $"**{name}**\n";
            }

            var ownPlaycount = artist.Playcount;
            var otherPlaycount = rightUserArtists.First(f => f.Name.Equals(name)).Playcount;

            if (ownPlaycount > otherPlaycount)
            {
                right += $"**{ownPlaycount}**";
            }
            else
            {
                right += $"{ownPlaycount}";
            }

            right += " • ";

            if (otherPlaycount > ownPlaycount)
            {
                right += $"**{otherPlaycount}**";
            }
            else
            {
                right += $"{otherPlaycount}";
            }
            right += $"\n";
        }

        var description = Description(leftUserArtists, timePeriod, matchedArtists);

        return new TasteModels
        {
            Description = description,
            LeftDescription = left,
            RightDescription = right
        };
    }

    public async Task<List<TopArtist>> GetUserAllTimeTopArtists(int userId, bool useCache = false)
    {
        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        var cacheKey = $"user-{userId}-topartists-alltime";
        if (this._cache.TryGetValue(cacheKey, out List<TopArtist> topArtists) && useCache)
        {
            return topArtists;
        }

        var freshTopArtists = (await ArtistRepository.GetUserArtists(userId, connection))
            .Select(s => new TopArtist
            {
                ArtistName = s.Name,
                UserPlaycount = s.Playcount
            })
            .OrderByDescending(o => o.UserPlaycount)
            .ToList();

        if (freshTopArtists.Count > 100)
        {
            this._cache.Set(cacheKey, freshTopArtists, TimeSpan.FromMinutes(10));
        }

        return freshTopArtists;
    }

    // Top artists for 2 users
    public (string result, int matches) GetTableTaste(IReadOnlyCollection<TasteItem> leftUserArtists, IReadOnlyCollection<TasteItem> rightUserArtists,
        int amount, TimePeriod timePeriod, string mainUser, string userToCompare, string type)
    {
        var artistsToShow = ArtistsToShow(leftUserArtists, rightUserArtists);

        var artists = artistsToShow.Select(s =>
        {
            var ownPlaycount = s.Playcount;
            var otherPlaycount = rightUserArtists.First(f => f.Name == s.Name).Playcount;

            return new TasteTwoUserModel
            {
                Artist = !string.IsNullOrWhiteSpace(s.Name) && s.Name.Length > AllowedCharacterCount(s.Name) ? $"{s.Name.Substring(0, AllowedCharacterCount(s.Name) - 2)}.." : s.Name,
                OwnPlaycount = ownPlaycount,
                OtherPlaycount = otherPlaycount
            };

            static int AllowedCharacterCount(string name)
            {
                return (StringExtensions.ContainsUnicodeCharacter(name) ? 10 : 18);
            }
        }).ToList();

        var description = new StringBuilder();
        description.AppendLine($"{Description(leftUserArtists, timePeriod, artistsToShow)}");

        var filterAmount = 0;
        for (var i = 0; i < 100; i++)
        {
            if (artists.Count(w => w.OwnPlaycount >= i && w.OtherPlaycount >= i) <= amount)
            {
                filterAmount = i;
                break;
            }
        }

        artists = artists.Where(w => w.OwnPlaycount >= filterAmount && w.OtherPlaycount >= filterAmount).ToList();

        if (artistsToShow.Count > 0)
        {
            var customTable = artists
                .Take(amount)
                .ToTasteTable(new[] { type, mainUser, "   ", userToCompare },
                    u => u.Artist,
                    u => u.OwnPlaycount,
                    u => GetCompareChar(u.OwnPlaycount, u.OtherPlaycount),
                    u => u.OtherPlaycount
                );

            description.Append($"```{customTable}```");
        }
        else
        {
            description.AppendLine();
            description.AppendLine($"No {type.ToLower()} matches... <:404:882220605783560222>");
        }

        return (description.ToString(), artistsToShow.Count);
    }

    private static string Description(IEnumerable<TasteItem> mainUserArtists, TimePeriod chartTimePeriod, IReadOnlyCollection<TasteItem> matchedArtists)
    {
        decimal percentage;

        if (!mainUserArtists.Any() || !matchedArtists.Any())
        {
            percentage = 0;
        }
        else
        {
            percentage = ((decimal)matchedArtists.Count / (decimal)mainUserArtists.Count()) * 100;
        }

        var description =
            $"**{matchedArtists.Count()}** ({percentage:0.0}%)  out of top **{mainUserArtists.Count()}** {chartTimePeriod.ToString().ToLower()} match";

        return description;
    }

    private static string GetCompareChar(long ownPlaycount, long otherPlaycount)
    {
        return ownPlaycount == otherPlaycount ? " • " : ownPlaycount > otherPlaycount ? " > " : " < ";
    }

    private static List<TasteItem> ArtistsToShow(IEnumerable<TasteItem> leftUserArtists, IEnumerable<TasteItem> rightUserArtists)
    {
        var artistsToShow =
            leftUserArtists
                .Where(w => rightUserArtists.Any(a => a.Name == w.Name))
                .OrderByDescending(o => o.Playcount)
                .ToList();
        return artistsToShow;
    }

    public TasteSettings SetTasteSettings(TasteSettings currentTasteSettings, string extraOptions)
    {
        var tasteSettings = currentTasteSettings;

        if (extraOptions == null)
        {
            return tasteSettings;
        }

        if (extraOptions.Contains("t") || extraOptions.Contains("table"))
        {
            tasteSettings.TasteType = TasteType.Table;
        }
        if (extraOptions.Contains("e") || extraOptions.Contains("embed") || extraOptions.Contains("embedfull") || extraOptions.Contains("fullembed"))
        {
            tasteSettings.TasteType = TasteType.FullEmbed;
        }
        if (extraOptions.Contains("xl") || extraOptions.Contains("xxl") || extraOptions.Contains("extralarge"))
        {
            tasteSettings.ExtraLarge = true;
        }

        return tasteSettings;
    }

    public async Task<string> GetCorrectedArtistName(string artistName)
    {
        var cachedArtistAliases = await GetCachedArtistAliases();
        var alias = cachedArtistAliases
            .FirstOrDefault(f => f.Alias.ToLower() == artistName.ToLower());

        var correctedArtistName = alias != null ? alias.Artist.Name : artistName;

        return correctedArtistName;
    }

    public async Task<Artist> GetArtistForId(int artistId)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();

        return await db.Artists.FindAsync(artistId);
    }

    public async Task<Artist> GetArtistFromDatabase(string artistName ,bool redirectsEnabled = true)
    {
        if (string.IsNullOrWhiteSpace(artistName))
        {
            return null;
        }

        string correctArtistName;
        if (redirectsEnabled)
        {
            correctArtistName = await GetCorrectedArtistName(artistName);
        }
        else
        {
            correctArtistName = artistName;
        }

        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        var artist = await this._artistRepository.GetArtistForName(correctArtistName, connection, true);

        await connection.CloseAsync();

        return artist?.SpotifyId != null ? artist : null;
    }

    private async Task<IReadOnlyList<ArtistAlias>> GetCachedArtistAliases()
    {
        if (this._cache.TryGetValue("artists", out IReadOnlyList<ArtistAlias> artists))
        {
            return artists;
        }

        await using var db = await this._contextFactory.CreateDbContextAsync();
        artists = await db.ArtistAliases
            .AsNoTracking()
            .Include(i => i.Artist)
            .ToListAsync();

        this._cache.Set("artists", artists, TimeSpan.FromHours(2));
        Log.Information($"Added {artists.Count} artists to memory cache");

        return artists;
    }


    public async Task<List<UserTrack>> GetTopTracksForArtist(int userId, string artistName)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        return await db.UserTracks
            .AsNoTracking()
            .Where(w => w.ArtistName.ToLower() == artistName.ToLower()
                        && w.UserId == userId)
            .OrderByDescending(o => o.Playcount)
            .ToListAsync();
    }

    public async Task<List<UserAlbum>> GetTopAlbumsForArtist(int userId, string artistName)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        return await db.UserAlbums
            .AsNoTracking()
            .Where(w => w.ArtistName.ToLower() == artistName.ToLower()
                        && w.UserId == userId)
            .OrderByDescending(o => o.Playcount)
            .ToListAsync();
    }

    public async Task<List<string>> GetLatestArtists(ulong discordUserId, bool cacheEnabled = true)
    {
        try
        {
            var cacheKey = $"user-recent-artists-{discordUserId}";

            var cacheAvailable = this._cache.TryGetValue(cacheKey, out List<string> userArtists);
            if (cacheAvailable && cacheEnabled)
            {
                return userArtists;
            }

            await using var db = await this._contextFactory.CreateDbContextAsync();
            var user = await db.Users.FirstOrDefaultAsync(f => f.DiscordUserId == discordUserId);

            if (user == null)
            {
                return new List<string> { Constants.AutoCompleteLoginRequired };
            }

            await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
            await connection.OpenAsync();

            var plays = await PlayRepository.GetUserPlaysWithinTimeRange(user.UserId, connection, DateTime.UtcNow.AddDays(-2));

            var artists = plays
                .OrderByDescending(o => o.TimePlayed)
                .Select(s => s.ArtistName.ToString())
                .Distinct()
                .ToList();

            this._cache.Set(cacheKey, artists, TimeSpan.FromSeconds(30));

            return artists;
        }
        catch (Exception e)
        {
            Log.Error("Error in GetLatestArtists", e);
            throw;
        }
    }

    public async Task<List<string>> GetRecentTopArtists(ulong discordUserId, bool cacheEnabled = true, int daysToGoBack = 20)
    {
        try
        {
            var cacheKey = $"user-recent-top-artists-{discordUserId}";

            var cacheAvailable = this._cache.TryGetValue(cacheKey, out List<string> userArtists);
            if (cacheAvailable && cacheEnabled)
            {
                return userArtists;
            }

            await using var db = await this._contextFactory.CreateDbContextAsync();
            var user = await db.Users.FirstOrDefaultAsync(f => f.DiscordUserId == discordUserId);

            if (user == null)
            {
                return new List<string> { Constants.AutoCompleteLoginRequired };
            }

            await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
            await connection.OpenAsync();

            var plays = await PlayRepository.GetUserPlaysWithinTimeRange(user.UserId, connection, DateTime.UtcNow.AddDays(-daysToGoBack));

            var artists = plays
                .GroupBy(g => g.ArtistName)
                .OrderByDescending(o => o.Count())
                .Select(s => s.Key)
                .ToList();

            this._cache.Set(cacheKey, artists, TimeSpan.FromSeconds(120));

            return artists;
        }
        catch (Exception e)
        {
            Log.Error("Error in GetRecentTopArtists", e);
            throw;
        }
    }

    public async Task<List<Artist>> SearchThroughArtists(string searchValue, bool cacheEnabled = true)
    {
        try
        {
            const string cacheKey = "artists-all";

            var cacheAvailable = this._cache.TryGetValue(cacheKey, out List<Artist> artists);
            if (!cacheAvailable && cacheEnabled)
            {
                const string sql = "SELECT * " +
                                   "FROM public.artists " +
                                   "WHERE popularity is not null AND popularity > 9 ";

                DefaultTypeMap.MatchNamesWithUnderscores = true;
                await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
                await connection.OpenAsync();

                artists = (await connection.QueryAsync<Artist>(sql)).ToList();

                this._cache.Set(cacheKey, artists, TimeSpan.FromHours(2));
            }

            searchValue = searchValue.ToLower();

            var results = artists.Where(w =>
                    w.Name.ToLower().StartsWith(searchValue))
                .OrderByDescending(o => o.Popularity)
                .ToList();

            results.AddRange(artists.Where(w =>
                    w.Name.ToLower().Contains(searchValue))
                .OrderByDescending(o => o.Popularity));

            return results;
        }
        catch (Exception e)
        {
            Log.Error("Error in SearchThroughArtists", e);
            throw;
        }
    }

    public static string IsArtistBirthday(DateTime? startDateTime = null)
    {
        if (!startDateTime.HasValue ||
            startDateTime.Value.Day == 1 && startDateTime.Value.Month == 1 ||
            startDateTime.Value.Day != DateTime.UtcNow.Day ||
            startDateTime.Value.Month != DateTime.UtcNow.Month)
        {
            return null;
        }

        return " 🎂";
    }
}
