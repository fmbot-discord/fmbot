using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using FMBot.Bot.Configurations;
using FMBot.Bot.Extensions;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.LastFM.Domain.Models;
using FMBot.LastFM.Domain.ResponseModels;
using FMBot.LastFM.Domain.Types;
using FMBot.LastFM.Services;
using FMBot.Persistence.Domain.Models;
using IF.Lastfm.Core.Api;
using IF.Lastfm.Core.Api.Enums;
using IF.Lastfm.Core.Api.Helpers;
using IF.Lastfm.Core.Objects;
using Track = FMBot.LastFM.Domain.Models.Track;

namespace FMBot.Bot.Services
{
    internal class LastFMService
    {
        private readonly LastfmClient _lastFMClient = new LastfmClient(ConfigData.Data.FMKey, ConfigData.Data.FMSecret);

        private readonly ILastfmApi _lastfmApi;

        public LastFMService(ILastfmApi lastfmApi)
        {
            this._lastfmApi = lastfmApi;
        }

        // Last scrobble
        public async Task<LastTrack> GetLastScrobbleAsync(string lastFMUserName)
        {
            var tracks = await this._lastFMClient.User.GetRecentScrobbles(lastFMUserName, null, 1, 1);
            Statistics.LastfmApiCalls.Inc();

            return tracks.Content[0];
        }

        // Recent scrobbles
        public async Task<PageResponse<LastTrack>> GetRecentScrobblesAsync(string lastFMUserName, int count = 2)
        {
            var recentScrobbles = await this._lastFMClient.User.GetRecentScrobbles(lastFMUserName, null, 1, count);
            Statistics.LastfmApiCalls.Inc();

            return recentScrobbles;
        }


        public static string TrackToLinkedString(LastTrack track)
        {
            if (track.Url.ToString().IndexOfAny(new[] { '(', ')' }) >= 0)
            {
                return TrackToString(track);
            }

            return $"[{track.Name}]({track.Url})\n" +
                   $"By **{track.ArtistName}**" +
                   (string.IsNullOrWhiteSpace(track.AlbumName)
                       ? "\n"
                       : $" | *{track.AlbumName}*\n");
        }

        public static string TrackToString(LastTrack track)
        {
            return $"{track.Name}\n" +
                   $"By **{track.ArtistName}**" +
                   (string.IsNullOrWhiteSpace(track.AlbumName)
                       ? "\n"
                       : $" | *{track.AlbumName}*\n");
        }

        public static string TrackToOneLinedString(LastTrack track)
        {
            return $"{track.Name} By **{track.ArtistName}**" +
                   (string.IsNullOrWhiteSpace(track.AlbumName)
                       ? ""
                       : $" | *{track.AlbumName}*");
        }

        public string TagsToLinkedString(Tags tags)
        {
            var tagString = "";
            for (var i = 0; i < tags.Tag.Length; i++)
            {
                if (i != 0)
                {
                    tagString += " - ";
                }
                var tag = tags.Tag[i];
                tagString += $"[{tag.Name}]({tag.Url})";
            }

            return tagString;
        }

        public string TopTagsToString(Toptags tags)
        {
            var tagString = "";
            for (var i = 0; i < tags.Tag.Length; i++)
            {
                if (i != 0)
                {
                    tagString += " - ";
                }
                var tag = tags.Tag[i];
                tagString += $"{tag.Name}";
            }

            return tagString;
        }

        // User
        public async Task<LastResponse<LastUser>> GetUserInfoAsync(string lastFMUserName)
        {
            var user = await this._lastFMClient.User.GetInfoAsync(lastFMUserName);
            Statistics.LastfmApiCalls.Inc();

            return user;
        }

        // Track info
        public async Task<Track> GetTrackInfoAsync(string trackName, string artistName, string username = null)
        {
            var queryParams = new Dictionary<string, string>
            {
                {"artist", artistName },
                {"track", trackName },
                {"username", username },
                {"autocorrect", "1"}
            };

            var trackCall = await this._lastfmApi.CallApiAsync<TrackResponse>(queryParams, Call.TrackInfo);
            Statistics.LastfmApiCalls.Inc();

            return !trackCall.Success ? null : trackCall.Content.Track;
        }

        public async Task<Response<ArtistResponse>> GetArtistInfoAsync(string artistName, string username = null)
        {
            var queryParams = new Dictionary<string, string>
            {
                {"artist", artistName },
                {"username", username },
                {"autocorrect", "1"}
            };

            var artistCall = await this._lastfmApi.CallApiAsync<ArtistResponse>(queryParams, Call.ArtistInfo);
            Statistics.LastfmApiCalls.Inc();

            return artistCall;
        }

        public async Task<Response<AlbumResponse>> GetAlbumInfoAsync(string artistName, string albumName, string username = null)
        {
            var queryParams = new Dictionary<string, string>
            {
                {"artist", artistName },
                {"album", albumName },
                {"username", username }
            };

            var albumCall = await this._lastfmApi.CallApiAsync<AlbumResponse>(queryParams, Call.AlbumInfo);
            Statistics.LastfmApiCalls.Inc();

            return albumCall;
        }

        // Album images
        public async Task<LastImageSet> GetAlbumImagesAsync(string artistName, string albumName)
        {
            var album = await this._lastFMClient.Album.GetInfoAsync(artistName, albumName);
            Statistics.LastfmApiCalls.Inc();

            return album?.Content?.Images;
        }

        public async Task<Bitmap> GetAlbumImageAsBitmapAsync(Uri largestImageSize)
        {
            try
            {
                var request = WebRequest.Create(largestImageSize);
                using var response = await request.GetResponseAsync();
                await using var responseStream = response.GetResponseStream();

                return new Bitmap(responseStream);
            }
            catch
            {
                return null;
            }
        }

        // Top albums
        public async Task<PageResponse<LastAlbum>> GetTopAlbumsAsync(string lastFMUserName, LastStatsTimeSpan timespan, int count = 2)
        {
            var topAlbums = await this._lastFMClient.User.GetTopAlbums(lastFMUserName, timespan, 1, count);
            Statistics.LastfmApiCalls.Inc();

            return topAlbums;
        }


        // Top artists
        public async Task<PageResponse<LastArtist>> GetTopArtistsAsync(string lastFMUserName,
            LastStatsTimeSpan timespan, int count = 2)
        {
            var topArtists = await this._lastFMClient.User.GetTopArtists(lastFMUserName, timespan, 1, count);
            Statistics.LastfmApiCalls.Inc();

            return topArtists;
        }

        // Top tracks
        public async Task<Response<TopTracksResponse>> GetTopTracksAsync(string lastFMUserName,
            string period, int count = 2)
        {
            var queryParams = new Dictionary<string, string>
            {
                {"limit", count.ToString() },
                {"username", lastFMUserName },
                {"period", period },
            };

            var artistCall = await this._lastfmApi.CallApiAsync<TopTracksResponse>(queryParams, Call.TopTracks);

            Statistics.LastfmApiCalls.Inc();

            return artistCall;
        }

        // Check if lastfm user exists
        public async Task<bool> LastFMUserExistsAsync(string lastFMUserName)
        {
            var lastFMUser = await this._lastFMClient.User.GetInfoAsync(lastFMUserName);
            Statistics.LastfmApiCalls.Inc();

            return lastFMUser.Success;
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

            var artists = artistsToShow.Select(s => new TasteTwoUserModel
            {
                Artist = !string.IsNullOrWhiteSpace(s.Name) && new StringInfo(s.Name).LengthInTextElements > 15 ? $"{s.Name.Substring(0, 14)}.." : s.Name,
                OwnPlaycount = s.PlayCount.Value,
                OtherPlaycount = rightUserArtists.Content.First(f => f.Name.Equals(s.Name)).PlayCount.Value
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

        public static TasteType StringToTasteType(string tasteTypeString)
        {
            if (Enum.TryParse(tasteTypeString, true, out TasteType tasteType))
            {
                return tasteType;
            }

            return tasteTypeString switch
            {
                "t" => TasteType.Table,
                "e" => TasteType.FullEmbed,
                "embed" => TasteType.FullEmbed,
                _ => TasteType.Table
            };
        }

        public static ChartTimePeriod StringToChartTimePeriod(string timeString)
        {
            if (Enum.TryParse(timeString, true, out ChartTimePeriod timePeriod))
            {
                return timePeriod;
            }

            return timeString switch
            {
                "w" => ChartTimePeriod.Weekly,
                "m" => ChartTimePeriod.Monthly,
                "q" => ChartTimePeriod.Quarterly,
                "h" => ChartTimePeriod.Half,
                "y" => ChartTimePeriod.Yearly,
                "a" => ChartTimePeriod.AllTime,
                "overall" => ChartTimePeriod.AllTime,
                _ => ChartTimePeriod.Weekly
            };
        }

        public static LastStatsTimeSpan ChartTimePeriodToLastStatsTimeSpan(ChartTimePeriod timePeriod)
        {
            return timePeriod switch
            {
                ChartTimePeriod.Weekly => LastStatsTimeSpan.Week,
                ChartTimePeriod.Monthly => LastStatsTimeSpan.Month,
                ChartTimePeriod.Quarterly => LastStatsTimeSpan.Quarter,
                ChartTimePeriod.Half => LastStatsTimeSpan.Half,
                ChartTimePeriod.Yearly => LastStatsTimeSpan.Year,
                ChartTimePeriod.AllTime => LastStatsTimeSpan.Overall,
                _ => LastStatsTimeSpan.Week
            };
        }

        public static string ChartTimePeriodToSiteTimePeriodUrl(ChartTimePeriod timePeriod)
        {
            return timePeriod switch
            {
                ChartTimePeriod.Weekly => "LAST_7_DAYS",
                ChartTimePeriod.Monthly => "LAST_30_DAYS",
                ChartTimePeriod.Quarterly => "LAST_90_DAYS",
                ChartTimePeriod.Half => "LAST_180_DAYS",
                ChartTimePeriod.Yearly => "LAST_365_DAYS",
                ChartTimePeriod.AllTime => "ALL",
                _ => "LAST_7_DAYS"
            };
        }

        public static string ChartTimePeriodToCallTimePeriod(ChartTimePeriod timePeriod)
        {
            return timePeriod switch
            {
                ChartTimePeriod.Weekly => TimePeriod.Week,
                ChartTimePeriod.Monthly => TimePeriod.Month,
                ChartTimePeriod.Quarterly => TimePeriod.Quarter,
                ChartTimePeriod.Half => TimePeriod.Half,
                ChartTimePeriod.Yearly => TimePeriod.Year,
                ChartTimePeriod.AllTime => TimePeriod.Overall,
                _ => TimePeriod.Week
            };
        }
    }
}
