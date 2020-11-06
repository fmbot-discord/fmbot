using System;
using System.Collections.Generic;
using System.Drawing;
using System.Net;
using System.Threading.Tasks;
using FMBot.Domain;
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

        // Recent scrobbles
        public async Task<long?> GetScrobbleCountFromDateAsync(string lastFMUserName, long? unixTimestamp)
        {
            var queryParams = new Dictionary<string, string>
            {
                {"user", lastFMUserName },
                {"limit", "1"}
            };

            if (unixTimestamp != null)
            {
                queryParams.Add("from", unixTimestamp.ToString());
            }

            var recentTracksCall = await this._lastfmApi.CallApiAsync<PlayResponse>(queryParams, Call.RecentTracks);
            Statistics.LastfmApiCalls.Inc();

            return recentTracksCall.Success ? recentTracksCall.Content.Recenttracks.Attr.Total : (long?)null;
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

        public static string ResponseTrackToLinkedString(ResponseTrack track)
        {
            if (track.Url.IndexOfAny(new[] { '(', ')' }) >= 0)
            {
                return ResponseTrackToString(track);
            }

            return $"[{track.Name}]({track.Url})\n" +
                   $"By **{track.Artist.Name}**" +
                   (track.Album == null || string.IsNullOrWhiteSpace(track.Album.Title)
                       ? "\n"
                       : $" | *{track.Album.Title}*\n");
        }

        public static string ResponseTrackToString(ResponseTrack track)
        {
            return $"{track.Name}\n" +
                   $"By **{track.Artist.Name}**" +
                   (track.Album == null || string.IsNullOrWhiteSpace(track.Album.Title)
                       ? "\n"
                       : $" | *{track.Album.Title}*\n");
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

        // User
        public async Task<LastFmUser> GetFullUserInfoAsync(string lastFMUserName)
        {
            var queryParams = new Dictionary<string, string>
            {
                {"user", lastFMUserName }
            };

            var userCall = await this._lastfmApi.CallApiAsync<UserResponse>(queryParams, Call.UserInfo);
            Statistics.LastfmApiCalls.Inc();

            return !userCall.Success ? null : userCall.Content.User;
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

        // Check if lastfm user exists
        public async Task<bool> LastFMUserExistsAsync(string lastFMUserName)
        {
            var lastFMUser = await this._lastFMClient.User.GetInfoAsync(lastFMUserName);
            Statistics.LastfmApiCalls.Inc();

            return lastFMUser.Success;
        }

        public async Task<Response<TokenResponse>> GetAuthToken()
        {
            var queryParams = new Dictionary<string, string>();

            var tokenCall = await this._lastfmApi.CallApiAsync<TokenResponse>(queryParams, Call.GetToken);
            Statistics.LastfmApiCalls.Inc();

            return tokenCall;
        }

        public async Task<Response<AuthSessionResponse>> GetAuthSession(string token)
        {
            var queryParams = new Dictionary<string, string>
            {
                {"token", token}
            };

            var authSessionCall = await this._lastfmApi.CallApiAsync<AuthSessionResponse>(queryParams, Call.GetAuthSession, true);
            Statistics.LastfmApiCalls.Inc();

            return authSessionCall;
        }

        public async Task<bool> LoveTrackAsync(User user, string artistName, string trackName)
        {
            var queryParams = new Dictionary<string, string>
            {
                {"artist", artistName},
                {"track", trackName},
                {"sk", user.SessionKeyLastFm},
            };

            var authSessionCall = await this._lastfmApi.CallApiAsync<AuthSessionResponse>(queryParams, Call.TrackLove, true);
            Statistics.LastfmApiCalls.Inc();

            return authSessionCall.Success;
        }

        public async Task<bool> UnLoveTrackAsync(User user, string artistName, string trackName)
        {
            var queryParams = new Dictionary<string, string>
            {
                {"artist", artistName},
                {"track", trackName},
                {"sk", user.SessionKeyLastFm},
            };

            var authSessionCall = await this._lastfmApi.CallApiAsync<AuthSessionResponse>(queryParams, Call.TrackUnLove, true);
            Statistics.LastfmApiCalls.Inc();

            return authSessionCall.Success;
        }
    }
}
