using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using FMBot.Domain;
using FMBot.Domain.Models;
using FMBot.LastFM.Domain.Models;
using FMBot.LastFM.Domain.ResponseModels;
using FMBot.LastFM.Domain.Types;
using FMBot.Persistence.Domain.Models;
using IF.Lastfm.Core.Api;
using IF.Lastfm.Core.Api.Enums;
using IF.Lastfm.Core.Api.Helpers;
using IF.Lastfm.Core.Objects;
using Microsoft.Extensions.Configuration;

namespace FMBot.LastFM.Services
{
    public class LastFMService
    {
        private readonly LastfmClient _lastFMClient;

        private readonly ILastfmApi _lastfmApi;

        private readonly string _key;
        private readonly string _secret;

        public LastFMService(IConfigurationRoot configuration, ILastfmApi lastfmApi)
        {
            this._key = configuration.GetSection("LastFm:Key").Value;
            this._secret = configuration.GetSection("LastFm:Secret").Value;
            this._lastFMClient = new LastfmClient(this._key, this._secret);
            this._lastfmApi = lastfmApi;
        }

        // Recent scrobbles
        public async Task<PageResponse<LastTrack>> GetRecentScrobblesAsync(string lastFMUserName, int count = 2)
        {
            var recentScrobbles = await this._lastFMClient.User.GetRecentScrobbles(lastFMUserName, null, count: count);
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

        public async Task<PageResponse<LastTrack>> SearchTrackAsync(string searchQuery)
        {
            var trackSearch = await this._lastFMClient.Track.SearchAsync(searchQuery, itemsPerPage: 1);
            Statistics.LastfmApiCalls.Inc();

            return trackSearch;
        }

        // Track info
        public async Task<ResponseTrack> GetTrackInfoAsync(string trackName, string artistName, string username = null)
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

        public async Task<PageResponse<LastAlbum>> SearchAlbumAsync(string searchQuery)
        {
            var albumSearch = await this._lastFMClient.Album.SearchAsync(searchQuery, itemsPerPage: 1);
            Statistics.LastfmApiCalls.Inc();

            return albumSearch;
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
            string period, int count = 2, int amountOfPages = 1)
        {
            var queryParams = new Dictionary<string, string>
            {
                {"limit", count.ToString() },
                {"username", lastFMUserName },
                {"period", period },
            };

            if (amountOfPages == 1)
            {
                Statistics.LastfmApiCalls.Inc();
                return await this._lastfmApi.CallApiAsync<TopTracksResponse>(queryParams, Call.TopTracks);
            }
            else
            {
                var response = await this._lastfmApi.CallApiAsync<TopTracksResponse>(queryParams, Call.TopTracks);
                if (response.Success && response.Content.TopTracks.Track.Count == 1000)
                {
                    for (var i = 1; i < amountOfPages; i++)
                    {
                        queryParams.Remove("page");
                        queryParams.Add("page", (i + 1).ToString());

                        var pageResponse = await this._lastfmApi.CallApiAsync<TopTracksResponse>(queryParams, Call.TopTracks);
                        Statistics.LastfmApiCalls.Inc();

                        if (pageResponse.Success)
                        {
                            response.Content.TopTracks.Track.AddRange(pageResponse.Content.TopTracks.Track);
                            if (pageResponse.Content.TopTracks.Track.Count < 1000)
                            {
                                break;
                            }
                        }
                        else
                        {
                            break;
                        }
                    }
                }

                return response;
            }
        }

        // Check if lastfm user exists
        public async Task<bool> LastFMUserExistsAsync(string lastFMUserName)
        {
            var lastFMUser = await this._lastFMClient.User.GetInfoAsync(lastFMUserName);
            Statistics.LastfmApiCalls.Inc();

            return lastFMUser.Success;
        }

        public static SettingsModel StringOptionsToSettings(
            string[] extraOptions,
            LastStatsTimeSpan defaultLastStatsTimeSpan = LastStatsTimeSpan.Week,
            ChartTimePeriod defaultChartTimePeriod = ChartTimePeriod.Weekly,
            string defaultUrlParameter = "LAST_7_DAYS",
            string defaultApiParameter = "7day")
        {
            var settingsModel = new SettingsModel();

            // time period
            if (extraOptions.Contains("weekly") || extraOptions.Contains("week") || extraOptions.Contains("w"))
            {
                settingsModel.LastStatsTimeSpan = LastStatsTimeSpan.Week;
                settingsModel.ChartTimePeriod = ChartTimePeriod.Weekly;
                settingsModel.Description = "Weekly";
                settingsModel.UrlParameter = "LAST_7_DAYS";
                settingsModel.ApiParameter = "7day";
            }
            else if (extraOptions.Contains("monthly") || extraOptions.Contains("month") || extraOptions.Contains("m"))
            {
                settingsModel.LastStatsTimeSpan = LastStatsTimeSpan.Month;
                settingsModel.ChartTimePeriod = ChartTimePeriod.Monthly;
                settingsModel.Description = "Monthly";
                settingsModel.UrlParameter = "LAST_30_DAYS";
                settingsModel.ApiParameter = "1month";
            }
            else if (extraOptions.Contains("quarterly") || extraOptions.Contains("quarter") || extraOptions.Contains("q"))
            {
                settingsModel.LastStatsTimeSpan = LastStatsTimeSpan.Quarter;
                settingsModel.ChartTimePeriod = ChartTimePeriod.Quarterly;
                settingsModel.Description = "Quarterly";
                settingsModel.UrlParameter = "LAST_90_DAYS";
                settingsModel.ApiParameter = "3month";
            }
            else if (extraOptions.Contains("halfyearly") || extraOptions.Contains("half") || extraOptions.Contains("h"))
            {
                settingsModel.LastStatsTimeSpan = LastStatsTimeSpan.Half;
                settingsModel.ChartTimePeriod = ChartTimePeriod.Half;
                settingsModel.Description = "Half-yearly";
                settingsModel.UrlParameter = "LAST_180_DAYS";
                settingsModel.ApiParameter = "6month";
            }
            else if (extraOptions.Contains("yearly") || extraOptions.Contains("year") || extraOptions.Contains("y"))
            {
                settingsModel.LastStatsTimeSpan = LastStatsTimeSpan.Year;
                settingsModel.ChartTimePeriod = ChartTimePeriod.Yearly;
                settingsModel.Description = "Yearly";
                settingsModel.UrlParameter = "LAST_365_DAYS";
                settingsModel.ApiParameter = "12month";
            }
            else if (extraOptions.Contains("overall") || extraOptions.Contains("alltime") || extraOptions.Contains("o") ||
                     extraOptions.Contains("at") ||
                     extraOptions.Contains("a"))
            {
                settingsModel.LastStatsTimeSpan = LastStatsTimeSpan.Overall;
                settingsModel.ChartTimePeriod = ChartTimePeriod.AllTime;
                settingsModel.Description = "Overall";
                settingsModel.UrlParameter = "ALL";
                settingsModel.ApiParameter = "overall";
            }
            else
            {
                settingsModel.LastStatsTimeSpan = defaultLastStatsTimeSpan;
                settingsModel.ChartTimePeriod = defaultChartTimePeriod;
                settingsModel.Description = "";
                settingsModel.UrlParameter = defaultUrlParameter;
                settingsModel.ApiParameter = defaultApiParameter;
            }

            settingsModel.Amount = 10;
            foreach (var extraOption in extraOptions)
            {
                if (int.TryParse(extraOption, out var result))
                {
                    if (result > 0 && result <= 50)
                    {
                        if (result > 16)
                        {
                            result = 16;
                        }

                        settingsModel.Amount = result;
                    }
                }

                if (extraOption.Contains("<@") || extraOption.Length == 18)
                {
                    var id = extraOption.Trim('@', '!', '<', '>');

                    if (ulong.TryParse(id, out var discordUserId))
                    {
                        settingsModel.OtherDiscordUserId = discordUserId;
                    }
                }
            }

            return settingsModel;
        }
    }
}
