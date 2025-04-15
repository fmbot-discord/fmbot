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
using FMBot.Domain.Flags;
using FMBot.Domain.Interfaces;
using FMBot.Domain.Models;
using FMBot.Domain.Types;
using FMBot.Persistence.Domain.Models;
using FMBot.Persistence.EntityFrameWork;
using FMBot.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Npgsql;
using Serilog;
using Web.InternalApi;

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
    private readonly UpdateService _updateService;
    private readonly AliasService _aliasService;
    private readonly UserService _userService;
    private readonly ArtistEnrichment.ArtistEnrichmentClient _artistEnrichment;

    public ArtistsService(IDbContextFactory<FMBotDbContext> contextFactory,
        IMemoryCache cache,
        IOptions<BotSettings> botSettings,
        ArtistRepository artistRepository,
        IDataSourceFactory dataSourceFactory,
        WhoKnowsArtistService whoKnowsArtistService,
        TimerService timer,
        UpdateService updateService,
        AliasService aliasService,
        UserService userService,
        ArtistEnrichment.ArtistEnrichmentClient artistEnrichment)
    {
        this._contextFactory = contextFactory;
        this._cache = cache;
        this._artistRepository = artistRepository;
        this._dataSourceFactory = dataSourceFactory;
        this._whoKnowsArtistService = whoKnowsArtistService;
        this._timer = timer;
        this._updateService = updateService;
        this._aliasService = aliasService;
        this._userService = userService;
        this._artistEnrichment = artistEnrichment;
        this._botSettings = botSettings.Value;
    }

    public async Task<ArtistSearch> SearchArtist(ResponseModel response, IUser discordUser, string artistValues,
        string lastFmUserName, string sessionKey = null, string otherUserUsername = null,
        bool useCachedArtists = false, int? userId = null, bool redirectsEnabled = true, ulong? interactionId = null,
        IUserMessage referencedMessage = null)
    {
        if (referencedMessage != null && string.IsNullOrWhiteSpace(artistValues))
        {
            var internalLookup = CommandContextExtensions.GetReferencedMusic(referencedMessage.Id)
                                 ??
                                 await this._userService.GetReferencedMusic(referencedMessage.Id);

            if (internalLookup?.Artist != null)
            {
                artistValues = internalLookup.Artist;
            }
        }

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
                artistCall =
                    await this._dataSourceFactory.GetArtistInfoAsync(artistValues, lastFmUserName, redirectsEnabled);
            }

            if (interactionId.HasValue)
            {
                PublicProperties.UsedCommandsArtists.TryAdd(interactionId.Value, artistValues);
                response.ReferencedMusic = new ReferencedMusic
                {
                    Artist = artistValues
                };
            }

            if (!artistCall.Success && artistCall.Error == ResponseStatus.MissingParameters)
            {
                response.Embed.WithDescription(
                    $"Artist `{artistValues}` could not be found, please check your search values and try again.");
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
                recentScrobbles =
                    await this._updateService.UpdateUser(new UpdateUserQueueItem(userId.Value,
                        getAccurateTotalPlaycount: false));
            }
            else
            {
                recentScrobbles =
                    await this._dataSourceFactory.GetRecentTracksAsync(lastFmUserName, 1, true, sessionKey);
            }

            if (GenericEmbedService.RecentScrobbleCallFailed(recentScrobbles))
            {
                var errorResponse =
                    GenericEmbedService.RecentScrobbleCallFailedResponse(recentScrobbles, lastFmUserName);
                return new ArtistSearch(null, errorResponse);
            }

            if (otherUserUsername != null)
            {
                lastFmUserName = otherUserUsername;
            }

            var lastPlayedTrack = recentScrobbles.Content.RecentTracks[0];
            Response<ArtistInfo> artistCall;
            if (useCachedArtists)
            {
                artistCall =
                    await GetCachedArtist(lastPlayedTrack.ArtistName, lastFmUserName, userId, redirectsEnabled);
            }
            else
            {
                artistCall = await this._dataSourceFactory.GetArtistInfoAsync(lastPlayedTrack.ArtistName,
                    lastFmUserName, redirectsEnabled);
            }

            if (interactionId.HasValue)
            {
                PublicProperties.UsedCommandsArtists.TryAdd(interactionId.Value, lastPlayedTrack.ArtistName);
                response.ReferencedMusic = new ReferencedMusic
                {
                    Artist = lastPlayedTrack.ArtistName
                };
            }

            if (artistCall.Content == null || !artistCall.Success)
            {
                response.Embed.WithDescription(
                    $"Last.fm did not return a result for **{lastPlayedTrack.ArtistName}**.");
                response.CommandResponse = CommandResponse.NotFound;
                response.ResponseType = ResponseType.Embed;
                return new ArtistSearch(null, response);
            }

            return new ArtistSearch(artistCall.Content, response);
        }
    }

    private async Task<Response<ArtistInfo>> GetCachedArtist(string artistName, string lastFmUserName,
        int? userId = null, bool redirectsEnabled = true)
    {
        Response<ArtistInfo> artistInfo;
        var cachedArtist = await GetArtistFromDatabase(artistName, redirectsEnabled);
        if (cachedArtist != null)
        {
            artistInfo = new Response<ArtistInfo>
            {
                Content = DatabaseArtistToArtistInfo(cachedArtist),
                Success = true
            };

            if (userId.HasValue)
            {
                var userPlaycount =
                    await this._whoKnowsArtistService.GetArtistPlayCountForUser(cachedArtist.Name, userId.Value);
                if (userPlaycount == 0)
                {
                    artistInfo =
                        await this._dataSourceFactory.GetArtistInfoAsync(artistName, lastFmUserName, redirectsEnabled);
                }
                else
                {
                    artistInfo.Content.UserPlaycount = userPlaycount;
                }
            }
        }
        else
        {
            artistInfo = await this._dataSourceFactory.GetArtistInfoAsync(artistName, lastFmUserName, redirectsEnabled);
        }

        return artistInfo;
    }

    private static ArtistInfo DatabaseArtistToArtistInfo(Artist artist)
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
        var request = new ArtistRequest
        {
            Artists =
            {
                topArtists.Select(s => new ArtistWithImage
                {
                    ArtistName = s.ArtistName,
                    ArtistImageUrl = s.ArtistImageUrl ?? ""
                })
            }
        };

        var artists = await this._artistEnrichment.AddMissingArtistImagesAsync(request);

        foreach (var topArtist in topArtists.Where(w => w.ArtistImageUrl == null))
        {
            var artist = artists.Artists.FirstOrDefault(f => !string.IsNullOrWhiteSpace(f.ArtistImageUrl) &&
                                                             f.ArtistName == topArtist.ArtistName);

            if (artist != null)
            {
                topArtist.ArtistImageUrl = artist.ArtistImageUrl;
            }
        }

        return topArtists;
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

    public async Task<List<ArtistPopularity>> GetArtistsPopularity(List<TopArtist> topArtists)
    {
        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        var artists = topArtists
            .GroupBy(g => g.ArtistName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                d => d.Key,
                d => d.OrderByDescending(o => o.UserPlaycount).First().UserPlaycount,
                StringComparer.OrdinalIgnoreCase
            );

        var artistNames = artists.Select(s => s.Key).ToList();
        var artistsWithPopularity = await ArtistRepository.GetArtistsPopularity(artistNames, connection);

        foreach (var artist in artistsWithPopularity)
        {
            if (artists.TryGetValue(artist.Name, out var playcount))
            {
                artist.Playcount = playcount;
            }
        }

        return artistsWithPopularity.ToList();
    }

    // Top artists for 2 users
    public (string result, int matches) GetTableTaste(IReadOnlyCollection<TasteItem> leftUserArtists,
        IReadOnlyCollection<TasteItem> rightUserArtists,
        int amount, TimePeriod timePeriod, string mainUser, string userToCompare, string type)
    {
        var artistsToShow = ArtistsToShow(leftUserArtists, rightUserArtists);

        var artists = artistsToShow.Select(s =>
        {
            var ownPlaycount = s.Playcount;
            var otherPlaycount = rightUserArtists.First(f => f.Name == s.Name).Playcount;

            return new TasteTwoUserModel
            {
                Artist = !string.IsNullOrWhiteSpace(s.Name) && s.Name.Length > AllowedCharacterCount(s.Name)
                    ? $"{s.Name.Substring(0, AllowedCharacterCount(s.Name) - 2)}.."
                    : s.Name,
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

    private static string Description(IEnumerable<TasteItem> mainUserArtists, TimePeriod chartTimePeriod,
        IReadOnlyCollection<TasteItem> matchedArtists)
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

    private static List<TasteItem> ArtistsToShow(IEnumerable<TasteItem> leftUserArtists,
        IEnumerable<TasteItem> rightUserArtists)
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

        if (extraOptions.Contains("e") || extraOptions.Contains("embed") || extraOptions.Contains("embedfull") ||
            extraOptions.Contains("fullembed"))
        {
            tasteSettings.TasteType = TasteType.FullEmbed;
        }

        if (extraOptions.Contains("xl") || extraOptions.Contains("xxl") || extraOptions.Contains("extralarge"))
        {
            tasteSettings.EmbedSize = EmbedSize.Large;
        }
        else if (extraOptions.Contains("xs") || extraOptions.Contains("xxs") || extraOptions.Contains("extrasmall"))
        {
            tasteSettings.EmbedSize = EmbedSize.Small;
        }

        return tasteSettings;
    }

    public async Task<Artist> GetArtistForId(int artistId)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();

        return await db.Artists.FindAsync(artistId);
    }

    public async Task<Artist> GetArtistFromDatabase(string artistName, bool redirectsEnabled = true)
    {
        if (string.IsNullOrWhiteSpace(artistName))
        {
            return null;
        }

        var alias = await this._aliasService.GetAlias(artistName);

        var correctArtistName = artistName;
        if (alias != null && redirectsEnabled && !alias.Options.HasFlag(AliasOption.NoRedirectInLastfmCalls))
        {
            correctArtistName = alias.ArtistName;
        }

        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        var artist = await ArtistRepository.GetArtistForName(correctArtistName, connection, true);

        await connection.CloseAsync();

        return artist?.SpotifyId != null ? artist : null;
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

    public async Task<int> GetUserAlbumCount(int userId)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        return await db.UserAlbums
            .AsNoTracking()
            .CountAsync(c => c.UserId == userId);
    }

    public async Task<int> GetUserTrackCount(int userId)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        return await db.UserTracks
            .AsNoTracking()
            .CountAsync(c => c.UserId == userId);
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

            var user = await this._userService.GetUserAsync(discordUserId);

            if (user == null)
            {
                return [Constants.AutoCompleteLoginRequired];
            }

            await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
            await connection.OpenAsync();

            var plays = await PlayRepository.GetUserPlaysWithinTimeRange(user.UserId, connection,
                DateTime.UtcNow.AddDays(-2));

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

    public async Task<List<TopArtist>> GetRecentTopArtists(ulong discordUserId, bool cacheEnabled = true,
        int daysToGoBack = 20)
    {
        try
        {
            var cacheKey = $"user-recent-top-artists-{discordUserId}";

            var cacheAvailable = this._cache.TryGetValue(cacheKey, out List<TopArtist> userArtists);
            if (cacheAvailable && cacheEnabled)
            {
                return userArtists;
            }

            var user = await this._userService.GetUserAsync(discordUserId);

            if (user == null)
            {
                return
                [
                    new TopArtist
                    {
                        ArtistName = Constants.AutoCompleteLoginRequired,
                        UserPlaycount = 1
                    }
                ];
            }

            await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
            await connection.OpenAsync();

            var plays = await PlayRepository.GetUserPlaysWithinTimeRange(user.UserId, connection,
                DateTime.UtcNow.AddDays(-daysToGoBack));

            var artists = plays
                .GroupBy(g => g.ArtistName)
                .OrderByDescending(o => o.Count())
                .Select(s => new TopArtist
                {
                    ArtistName = s.Key,
                    UserPlaycount = s.Count()
                })
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
                const string sql = "SELECT name, popularity " +
                                   "FROM public.artists " +
                                   "WHERE popularity is not null AND popularity > 9 ";

                DefaultTypeMap.MatchNamesWithUnderscores = true;
                await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
                await connection.OpenAsync();

                artists = (await connection.QueryAsync<Artist>(sql)).ToList();

                this._cache.Set(cacheKey, artists, TimeSpan.FromHours(2));
            }

            var results = artists.Where(w =>
                    w.Name.StartsWith(searchValue, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(o => o.Popularity)
                .ToList();

            results.AddRange(artists.Where(w =>
                    w.Name.Contains(searchValue, StringComparison.OrdinalIgnoreCase))
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
