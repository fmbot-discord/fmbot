using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using FMBot.Bot.Configurations;
using FMBot.Bot.Extensions;
using FMBot.Bot.Models;
using FMBot.Domain.Models;
using FMBot.Persistence.Domain.Models;
using FMBot.Persistence.EntityFrameWork;
using FMBot.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;
using Serilog;

namespace FMBot.Bot.Services
{
    public class ArtistsService
    {
        private readonly IDbContextFactory<FMBotDbContext> _contextFactory;
        private readonly IMemoryCache _cache;
        private readonly BotSettings _botSettings;
        private readonly ArtistRepository _artistRepository;

        public ArtistsService(IDbContextFactory<FMBotDbContext> contextFactory, IMemoryCache cache, IOptions<BotSettings> botSettings, ArtistRepository artistRepository)
        {
            this._contextFactory = contextFactory;
            this._cache = cache;
            this._artistRepository = artistRepository;
            this._botSettings = botSettings.Value;
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
                var url = topArtist.ArtistUrl.ToLower();
                var artistImage = (string)this._cache.Get(CacheKeyForArtist(url));

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

            const string sql = "SELECT LOWER(last_fm_url) as last_fm_url, LOWER(spotify_image_url) as spotify_image_url " +
                               "FROM public.artists where last_fm_url is not null and spotify_image_url is not null;";

            DefaultTypeMap.MatchNamesWithUnderscores = true;
            await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
            await connection.OpenAsync();

            var artistCovers = (await connection.QueryAsync<ArtistSpotifyCoverDto>(sql)).ToList();

            foreach (var artistCover in artistCovers)
            {
                this._cache.Set(CacheKeyForArtist(artistCover.LastFmUrl), artistCover.SpotifyImageUrl, cacheTime);
            }

            this._cache.Set(cacheKey, true, cacheTime);
        }

        public static string CacheKeyForArtist(string lastFmUrl)
        {
            return $"artist-spotify-image-{lastFmUrl.ToLower()}";
        }

        // Top artists for 2 users
        public TasteModels GetEmbedTaste(TopArtistList leftUserArtists,
            TopArtistList rightUserArtists, int amount, TimePeriod timePeriod)
        {
            var matchedArtists = ArtistsToShow(leftUserArtists.TopArtists, rightUserArtists.TopArtists);

            var left = "";
            var right = "";
            foreach (var artist in matchedArtists.Take(amount))
            {
                var name = artist.ArtistName;
                if (!string.IsNullOrWhiteSpace(name) && name.Length > 24)
                {
                    left += $"**{name.Substring(0, 24)}..**\n";
                }
                else
                {
                    left += $"**{name}**\n";
                }

                var ownPlaycount = artist.UserPlaycount.Value;
                var otherPlaycount = rightUserArtists.TopArtists.First(f => f.ArtistName.Equals(name)).UserPlaycount.Value;

                if (matchedArtists.Count > 30 && otherPlaycount < 5)
                {
                    continue;
                }

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

            var description = Description(leftUserArtists.TopArtists, timePeriod, matchedArtists);

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

            var freshTopArtists = (await this._artistRepository.GetUserArtists(userId, connection))
                .Select(s => new TopArtist
                {
                    ArtistName = s.Name,
                    UserPlaycount = s.Playcount
                }).ToList();

            if (freshTopArtists.Count > 100)
            {
                this._cache.Set(cacheKey, freshTopArtists, TimeSpan.FromMinutes(10));
            }

            return freshTopArtists;
        }

        // Top artists for 2 users
        public string GetTableTaste(TopArtistList leftUserArtists,
            TopArtistList rightUserArtists, int amount, TimePeriod timePeriod, string mainUser, string userToCompare)
        {
            var artistsToShow = ArtistsToShow(leftUserArtists.TopArtists, rightUserArtists.TopArtists);

            var artists = artistsToShow.Select(s =>
            {
                var ownPlaycount = s.UserPlaycount.Value;
                var otherPlaycount = rightUserArtists.TopArtists.First(f => f.ArtistName.Equals(s.ArtistName)).UserPlaycount.Value;

                return new TasteTwoUserModel
                {
                    Artist = !string.IsNullOrWhiteSpace(s.ArtistName) && s.ArtistName.Length > AllowedCharacterCount(s.ArtistName) ? $"{s.ArtistName.Substring(0, AllowedCharacterCount(s.ArtistName) - 2)}…" : s.ArtistName,
                    OwnPlaycount = ownPlaycount,
                    OtherPlaycount = otherPlaycount
                };

                static int AllowedCharacterCount(string name)
                {
                    return (StringExtensions.ContainsUnicodeCharacter(name) ? 9 : 16);
                }
            }).ToList();

            if (artists.Count > 25)
            {
                artists = artists.Where(w => w.OtherPlaycount > 4).ToList();
            }

            var customTable = artists
                .Take(amount)
                .ToTasteTable(new[] { "Artist", mainUser, "   ", userToCompare },
                    u => u.Artist,
                    u => u.OwnPlaycount,
                    u => GetCompareChar(u.OwnPlaycount, u.OtherPlaycount),
                    u => u.OtherPlaycount
                );


            var description = $"{Description(leftUserArtists.TopArtists, timePeriod, artistsToShow)}\n" +
                              $"```{customTable}```";

            return description;
        }

        private static string Description(IEnumerable<TopArtist> mainUserArtists, TimePeriod chartTimePeriod, ICollection<TopArtist> matchedArtists)
        {
            var percentage = ((decimal)matchedArtists.Count / (decimal)mainUserArtists.Count()) * 100;
            var description =
                $"**{matchedArtists.Count()}** ({percentage:0.0}%)  out of top **{mainUserArtists.Count()}** {chartTimePeriod.ToString().ToLower()} artists match";

            return description;
        }

        private static string GetCompareChar(long ownPlaycount, long otherPlaycount)
        {
            return ownPlaycount == otherPlaycount ? " • " : ownPlaycount > otherPlaycount ? " > " : " < ";
        }

        private static IList<TopArtist> ArtistsToShow(IEnumerable<TopArtist> leftUserArtists, IEnumerable<TopArtist> rightUserArtists)
        {
            var artistsToShow =
                leftUserArtists
                    .Where(w => rightUserArtists.Select(s => s.ArtistName).Contains(w.ArtistName))
                    .OrderByDescending(o => o.UserPlaycount)
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

            return tasteSettings;
        }

        public async Task<Artist> GetArtistFromDatabase(string artistName)
        {
            if (string.IsNullOrWhiteSpace(artistName))
            {
                return null;
            }

            var cachedArtistAliases = await GetCachedArtistAliases();
            var alias = cachedArtistAliases
                .FirstOrDefault(f => f.Alias.ToLower() == artistName.ToLower());

            var correctedArtistName = alias != null ? alias.Artist.Name : artistName;

            await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
            await connection.OpenAsync();

            var artist = await this._artistRepository.GetArtistForName(correctedArtistName, connection, true);

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
                    return new List<string> { "Login to the bot first" };
                }

                const string sql = "SELECT * " +
                                   "FROM public.user_plays " +
                                   "WHERE user_id = @userId " +
                                   "ORDER BY time_played desc " +
                                   "LIMIT 50 ";

                DefaultTypeMap.MatchNamesWithUnderscores = true;
                await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
                await connection.OpenAsync();

                var userPlays = (await connection.QueryAsync<UserPlay>(sql, new
                {
                    userId = user.UserId
                })).ToList();

                var artists = userPlays
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

        public async Task<List<string>> GetRecentTopArtists(ulong discordUserId, bool cacheEnabled = true)
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
                    return new List<string> { "Login to the bot first" };
                }

                const string sql = "SELECT * " +
                                   "FROM public.user_plays " +
                                   "WHERE user_id = @userId " +
                                   "ORDER BY time_played desc " +
                                   "LIMIT 1500 ";

                DefaultTypeMap.MatchNamesWithUnderscores = true;
                await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
                await connection.OpenAsync();

                var userPlays = (await connection.QueryAsync<UserPlay>(sql, new
                {
                    userId = user.UserId
                })).ToList();

                var artists = userPlays
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

                    this._cache.Set(cacheKey, artists, TimeSpan.FromMinutes(15));
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
    }
}
