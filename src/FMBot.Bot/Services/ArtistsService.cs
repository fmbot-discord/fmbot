using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FMBot.Bot.Extensions;
using FMBot.Bot.Models;
using FMBot.Domain.Models;
using FMBot.Persistence.Domain.Models;
using IF.Lastfm.Core.Api.Helpers;
using IF.Lastfm.Core.Objects;

namespace FMBot.Bot.Services
{
    public class ArtistsService
    {
        // Top artists for 2 users
        public async Task<TasteModels> GetEmbedTasteAsync(PageResponse<LastArtist> leftUserArtists,
            PageResponse<LastArtist> rightUserArtists, int amount, ChartTimePeriod timePeriod)
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
        public async Task<string> GetTableTasteAsync(PageResponse<LastArtist> leftUserArtists,
            PageResponse<LastArtist> rightUserArtists, int amount, ChartTimePeriod timePeriod, string mainUser, string userToCompare)
        {
            var artistsToShow = ArtistsToShow(leftUserArtists, rightUserArtists);

            var artists = artistsToShow.Select(s =>
            {
                return new TasteTwoUserModel
                {
                    Artist = !string.IsNullOrWhiteSpace(s.Name) && s.Name.Length > AllowedCharacterCount(s.Name) ? $"{s.Name.Substring(0, AllowedCharacterCount(s.Name) - 2)}…" : s.Name,
                    OwnPlaycount = s.PlayCount.Value,
                    OtherPlaycount = rightUserArtists.Content.First(f => f.Name.Equals(s.Name)).PlayCount.Value
                };

                static int AllowedCharacterCount(string name)
                {
                    return (StringExtensions.ContainsUnicodeCharacter(name) ? 9 : 16);
                }
            });

            var customTable = artists.Take(amount).ToTasteTable(new[] { "Artist", mainUser, "   ", userToCompare },
                u => u.Artist,
                u => u.OwnPlaycount,
                u => this.GetCompareChar(u.OwnPlaycount, u.OtherPlaycount),
                u => u.OtherPlaycount
            );


            var description = $"{Description(leftUserArtists, timePeriod, artistsToShow)}\n" +
                              $"```{customTable}```";

            return description;
        }

        private static string Description(IEnumerable<LastArtist> mainUserArtists, ChartTimePeriod chartTimePeriod, IOrderedEnumerable<LastArtist> matchedArtists)
        {
            var percentage = ((decimal)matchedArtists.Count() / (decimal)mainUserArtists.Count()) * 100;
            var description =
                $"**{matchedArtists.Count()}** ({percentage:0.0}%)  out of top **{mainUserArtists.Count()}** {chartTimePeriod.ToString().ToLower()} artists match";

            return description;
        }

        private string GetCompareChar(int ownPlaycount, int otherPlaycount)
        {
            return ownPlaycount == otherPlaycount ? " • " : ownPlaycount > otherPlaycount ? " > " : " < ";
        }

        private IOrderedEnumerable<LastArtist> ArtistsToShow(IEnumerable<LastArtist> pageResponse, IPageResponse<LastArtist> lastArtists)
        {
            var artistsToShow =
                pageResponse
                    .Where(w => lastArtists.Content.Select(s => s.Name).Contains(w.Name))
                    .OrderByDescending(o => o.PlayCount);
            return artistsToShow;
        }

        public TasteSettings SetTasteSettings(TasteSettings currentTasteSettings, string[] extraOptions)
        {
            var tasteSettings = currentTasteSettings;

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
    }
}
