using System;
using System.Collections.Generic;
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

            var albumCovers = await GetCachedArtistImages();

            foreach (var topArtist in topArtists.Where(w => w.ArtistImageUrl == null))
            {
                var url = topArtist.ArtistUrl.ToLower();
                var artistImage = albumCovers.FirstOrDefault(item => item.LastFmUrl.Equals(url));

                if (artistImage != null)
                {
                    topArtist.ArtistImageUrl = artistImage.SpotifyImageUrl;
                }
            }

            return topArtists;
        }

        private async Task<List<ArtistSpotifyCoverDto>> GetCachedArtistImages()
        {
            const string cacheKey = "artist-spotify-covers";
            if (this._cache.TryGetValue(cacheKey, out List<ArtistSpotifyCoverDto> artistCovers))
            {
                return artistCovers;
            }

            const string sql = "SELECT LOWER(last_fm_url) as last_fm_url, LOWER(spotify_image_url) as spotify_image_url " +
                               "FROM public.artists where last_fm_url is not null and spotify_image_url is not null;";

            DefaultTypeMap.MatchNamesWithUnderscores = true;
            await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
            await connection.OpenAsync();

            artistCovers = (await connection.QueryAsync<ArtistSpotifyCoverDto>(sql)).ToList();

            this._cache.Set(cacheKey, artistCovers, TimeSpan.FromMinutes(1));

            return artistCovers;
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

            var artist = await this._artistRepository.GetArtistForName(correctedArtistName);

            return artist?.SpotifyId != null ? artist : null;
        }

        private async Task<IReadOnlyList<ArtistAlias>> GetCachedArtistAliases()
        {
            if (this._cache.TryGetValue("artists", out IReadOnlyList<ArtistAlias> artists))
            {
                return artists;
            }

            await using var db = this._contextFactory.CreateDbContext();
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
            await using var db = this._contextFactory.CreateDbContext();
            return await db.UserTracks
                .AsNoTracking()
                .Where(w => w.ArtistName.ToLower() == artistName.ToLower()
                            && w.UserId == userId)
                .OrderByDescending(o => o.Playcount)
                .ToListAsync();
        }

        public async Task<List<UserAlbum>> GetTopAlbumsForArtist(int userId, string artistName)
        {
            await using var db = this._contextFactory.CreateDbContext();
            return await db.UserAlbums
                .AsNoTracking()
                .Where(w => w.ArtistName.ToLower() == artistName.ToLower()
                            && w.UserId == userId)
                .OrderByDescending(o => o.Playcount)
                .Take(12)
                .ToListAsync();
        }
    }
}
