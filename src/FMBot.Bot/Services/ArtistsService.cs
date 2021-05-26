using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FMBot.Bot.Extensions;
using FMBot.Bot.Models;
using FMBot.Domain.Models;
using FMBot.Persistence.Domain.Models;
using FMBot.Persistence.EntityFrameWork;
using IF.Lastfm.Core.Api.Helpers;
using IF.Lastfm.Core.Objects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Serilog;

namespace FMBot.Bot.Services
{
    public class ArtistsService
    {
        private readonly IDbContextFactory<FMBotDbContext> _contextFactory;
        private readonly IMemoryCache _cache;

        public ArtistsService(IDbContextFactory<FMBotDbContext> contextFactory, IMemoryCache cache)
        {
            this._contextFactory = contextFactory;
            this._cache = cache;
        }

        // Top artists for 2 users
        public TasteModels GetEmbedTaste(PageResponse<LastArtist> leftUserArtists,
            PageResponse<LastArtist> rightUserArtists, int amount, TimePeriod timePeriod)
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

                var ownPlaycount = artist.PlayCount.Value;
                var otherPlaycount = rightUserArtists.Content.First(f => f.Name.Equals(name)).PlayCount.Value;

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

            var description = Description(leftUserArtists, timePeriod, matchedArtists);

            return new TasteModels
            {
                Description = description,
                LeftDescription = left,
                RightDescription = right
            };
        }

        // Top artists for 2 users
        public string GetTableTaste(PageResponse<LastArtist> leftUserArtists,
            PageResponse<LastArtist> rightUserArtists, int amount, TimePeriod timePeriod, string mainUser, string userToCompare)
        {
            var artistsToShow = ArtistsToShow(leftUserArtists, rightUserArtists);

            var artists = artistsToShow.Select(s =>
            {
                var ownPlaycount = s.PlayCount.Value;
                var otherPlaycount = rightUserArtists.Content.First(f => f.Name.Equals(s.Name)).PlayCount.Value;

                return new TasteTwoUserModel
                {
                    Artist = !string.IsNullOrWhiteSpace(s.Name) && s.Name.Length > AllowedCharacterCount(s.Name) ? $"{s.Name.Substring(0, AllowedCharacterCount(s.Name) - 2)}…" : s.Name,
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


            var description = $"{Description(leftUserArtists, timePeriod, artistsToShow)}\n" +
                              $"```{customTable}```";

            return description;
        }

        private static string Description(IEnumerable<LastArtist> mainUserArtists, TimePeriod chartTimePeriod, IList<LastArtist> matchedArtists)
        {
            var percentage = ((decimal)matchedArtists.Count / (decimal)mainUserArtists.Count()) * 100;
            var description =
                $"**{matchedArtists.Count()}** ({percentage:0.0}%)  out of top **{mainUserArtists.Count()}** {chartTimePeriod.ToString().ToLower()} artists match";

            return description;
        }

        private static string GetCompareChar(int ownPlaycount, int otherPlaycount)
        {
            return ownPlaycount == otherPlaycount ? " • " : ownPlaycount > otherPlaycount ? " > " : " < ";
        }

        private IList<LastArtist> ArtistsToShow(IEnumerable<LastArtist> leftUserArtists, IPageResponse<LastArtist> rightUserArtists)
        {
            var artistsToShow =
                leftUserArtists
                    .Where(w => rightUserArtists.Content.Select(s => s.Name).Contains(w.Name))
                    .OrderByDescending(o => o.PlayCount)
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

            await using var db = this._contextFactory.CreateDbContext();
            var artist = await db.Artists
                .AsNoTracking()
                .FirstOrDefaultAsync(f => EF.Functions.ILike(f.Name, correctedArtistName));

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
