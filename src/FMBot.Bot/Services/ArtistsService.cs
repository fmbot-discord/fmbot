using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using FMBot.Bot.Extensions;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.LastFM.Domain.Models;
using FMBot.Persistence.Domain.Models;
using FMBot.Persistence.EntityFrameWork;
using IF.Lastfm.Core.Api.Helpers;
using IF.Lastfm.Core.Objects;
using Microsoft.EntityFrameworkCore;

namespace FMBot.Bot.Services
{
    public class ArtistsService
    {
        public static IList<ArtistWithUser> AddUserToIndexList(IList<ArtistWithUser> artists, User userSettings, IGuildUser user, ArtistResponse artist)
        {
            artists.Add(new ArtistWithUser
            {
                UserId = userSettings.UserId,
                ArtistName = artist.Artist.Name,
                Playcount = Convert.ToInt32(artist.Artist.Stats.Userplaycount.Value),
                LastFMUsername = userSettings.UserNameLastFM,
                DiscordUserId = userSettings.DiscordUserId,
                DiscordName = user.Nickname ?? user.Username
            });

            return artists.OrderByDescending(o => o.Playcount).ToList();
        }

        public static string ArtistWithUserToStringList(IList<ArtistWithUser> artists, ArtistResponse artistResponse, int userId)
        {
            var reply = "";

            var artistsCount = artists.Count;
            if (artistsCount > 14)
            {
                artistsCount = 14;
            }

            for (var index = 0; index < artistsCount; index++)
            {
                var artist = artists[index];

                var nameWithLink = NameWithLink(artist);
                var playString = GetPlaysString(artist.Playcount);

                if (index == 0)
                {
                    reply += $"👑  {nameWithLink}";
                }
                else
                {
                    reply += $" {index + 1}.  {nameWithLink} ";
                }
                if (artist.UserId != userId)
                {
                    reply += $"- **{artist.Playcount}** {playString}\n";
                }
                else
                {
                    reply += $"- **{artistResponse.Artist.Stats.Userplaycount}** {playString}\n";
                }
            }

            if (artists.Count == 1)
            {
                reply += $"\nNobody else has this artist in their top {Constants.ArtistsToIndex} artists.";
            }

            return reply;
        }

        private static string NameWithLink(ArtistWithUser artist)
        {
            var discordName = artist.DiscordName.Replace("(", "").Replace(")", "").Replace("[", "").Replace("]", "");
            var nameWithLink = $"[{discordName}]({Constants.LastFMUserUrl}{artist.LastFMUsername})";
            return nameWithLink;
        }

        private static string GetPlaysString(int artistPlaycount)
        {
            return artistPlaycount == 1 ? "play" : "plays";
        }

        public async Task<IList<ArtistWithUser>> GetIndexedUsersForArtist(ICommandContext context,
            IReadOnlyList<User> guildUsers, string artistName)
        {
            var userIds = guildUsers.Select(s => s.UserId);

            await using var db = new FMBotDbContext();
            var artists = await db.Artists
                .Include(i => i.User)
                .Where(w => w.Name.ToLower() == artistName.ToLower()
                            && userIds.Contains(w.UserId))
                .OrderByDescending(o => o.Playcount)
                .Take(14)
                .ToListAsync();

            var returnArtists = new List<ArtistWithUser>();

            foreach (var artist in artists)
            {
                var discordUser = await context.Guild.GetUserAsync(artist.User.DiscordUserId);
                if (discordUser != null)
                {
                    returnArtists.Add(new ArtistWithUser
                    {
                        ArtistName = artist.Name,
                        DiscordName = discordUser.Nickname ?? discordUser.Username,
                        Playcount = artist.Playcount,
                        DiscordUserId = artist.User.DiscordUserId,
                        LastFMUsername = artist.User.UserNameLastFM,
                        UserId = artist.UserId,
                    });
                }
            }

            return returnArtists;
        }

        public async Task<IList<ListArtist>> GetTopArtistsForGuild(IReadOnlyList<User> guildUsers)
        {
            var userIds = guildUsers.Select(s => s.UserId);

            await using var db = new FMBotDbContext();
            return await db.Artists
                .AsQueryable()
                .Where(w => userIds.Contains(w.UserId))
                .GroupBy(o => o.Name)
                .OrderByDescending(o => o.Sum(s => s.Playcount))
                .Take(14)
                .Select(s => new ListArtist
                {
                    ArtistName = s.Key,
                    Playcount = s.Sum(s => s.Playcount),
                    ListenerCount = s.Count()
                })
                .ToListAsync();
        }


        public async Task<int> GetArtistListenerCountForServer(IReadOnlyList<User> guildUsers, string artistName)
        {
            var userIds = guildUsers.Select(s => s.UserId);

            await using var db = new FMBotDbContext();
            return await db.Artists
                .AsQueryable()
                .Where(w => w.Name.ToLower() == artistName.ToLower()
                            && userIds.Contains(w.UserId))
                .CountAsync();
        }

        public async Task<int> GetArtistPlayCountForServer(IReadOnlyList<User> guildUsers, string artistName)
        {
            var userIds = guildUsers.Select(s => s.UserId);

            await using var db = new FMBotDbContext();
            var query = db.Artists
                .AsQueryable()
                .Where(w => w.Name.ToLower() == artistName.ToLower()
                            && userIds.Contains(w.UserId));

            // This is bad practice, but it helps with speed. An exception gets thrown if the artist does not exist in the database.
            // Checking if the records exist first would be an extra database call
            try
            {
                return await query.SumAsync(s => s.Playcount);
            }
            catch
            {
                return 0;
            }
        }

        public async Task<double> GetArtistAverageListenerPlaycountForServer(IReadOnlyList<User> guildUsers, string artistName)
        {
            var userIds = guildUsers.Select(s => s.UserId);

            await using var db = new FMBotDbContext();
            var query = db.Artists
                .AsQueryable()
                .Where(w => w.Name.ToLower() == artistName.ToLower()
                            && userIds.Contains(w.UserId));

            try
            {
                return await query.AverageAsync(s => s.Playcount);
            }
            catch
            {
                return 0;
            }
        }


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
                    Artist = !string.IsNullOrWhiteSpace(s.Name) && s.Name.Length > AllowedCharacterCount(s.Name) ? $"{s.Name.Substring(0, AllowedCharacterCount(s.Name) - 2)}.." : s.Name,
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
