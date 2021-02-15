using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;
using FMBot.Domain;
using FMBot.Domain.Models;
using FMBot.LastFM.Domain.Models;
using FMBot.LastFM.Domain.Types;
using FMBot.Persistence.Domain.Models;
using IF.Lastfm.Core.Api;
using IF.Lastfm.Core.Api.Enums;
using IF.Lastfm.Core.Api.Helpers;
using IF.Lastfm.Core.Objects;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;

namespace FMBot.LastFM.Services
{
    public class LastFmService
    {
        private readonly LastfmClient _lastFmClient;
        private readonly IMemoryCache _cache;
        private readonly ILastfmApi _lastFmApi;

        public LastFmService(IConfigurationRoot configuration, ILastfmApi lastFmApi, IMemoryCache cache)
        {
            this._lastFmClient =
                new LastfmClient(configuration.GetSection("LastFm:Key").Value, configuration.GetSection("LastFm:Secret").Value);
            this._lastFmApi = lastFmApi;
            this._cache = cache;
        }

        // Recent scrobbles
        public async Task<PageResponse<LastTrack>> GetRecentScrobblesAsync(string lastFmUserName, int count = 2)
        {
            var recentScrobbles = await this._lastFmClient.User.GetRecentScrobbles(lastFmUserName, null, count: count);
            Statistics.LastfmApiCalls.Inc();

            return recentScrobbles;
        }

        // Recent scrobbles
        public async Task<Response<RecentTrackList>> GetRecentTracksAsync(string lastFmUserName, int count = 2, bool useCache = false, string sessionKey = null, long? fromUnixTimestamp = null)
        {
            var cacheKey = $"{lastFmUserName}-lastfm-recent-tracks";
            var queryParams = new Dictionary<string, string>
            {
                {"user", lastFmUserName },
                {"limit", count.ToString()}
            };
            var authorizedCall = false;

            if (!string.IsNullOrEmpty(sessionKey))
            {
                queryParams.Add("sk", sessionKey);
                authorizedCall = true;
            }
            if (fromUnixTimestamp != null)
            {
                queryParams.Add("from", fromUnixTimestamp.ToString());
            }

            if (useCache)
            {
                var cachedRecentTracks = this._cache.TryGetValue(cacheKey, out RecentTrackList recentTrackResponse);
                if (cachedRecentTracks && recentTrackResponse.RecentTracks.Any() && recentTrackResponse.RecentTracks.Count >= count)
                {
                    return new Response<RecentTrackList>
                    {
                        Content = recentTrackResponse,
                        Success = true
                    };
                }
            }

            var recentTracksCall = await this._lastFmApi.CallApiAsync<RecentTracksListLfmResponseModel>(queryParams, Call.RecentTracks, authorizedCall);

            if (recentTracksCall.Success)
            {
                var response = new Response<RecentTrackList>
                {
                    Content = new RecentTrackList
                    {
                        TotalAmount = recentTracksCall.Content.RecentTracks.AttributesLfm.Total,
                        UserUrl = $"https://www.last.fm/user/{lastFmUserName}",
                        UserRecentTracksUrl = $"https://www.last.fm/user/{lastFmUserName}/library",
                        RecentTracks = recentTracksCall.Content.RecentTracks.Track.Select(s =>
                            new RecentTrack
                            {
                                TrackName = s.Name,
                                TrackUrl = s.Url.ToString(),
                                ArtistName = s.Artist.Text,
                                ArtistUrl = s.Artist.Url,
                                AlbumName = !string.IsNullOrWhiteSpace(s.Album?.Text) ? s.Album.Text : null,
                                AlbumUrl = s.Album?.Text,
                                AlbumCoverUrl = s.Image?.FirstOrDefault(a => a.Size == "extralarge") != null &&
                                                !s.Image.First(a => a.Size == "extralarge").Text.Contains(Constants.LastFmNonExistentImageName)
                                    ? s.Image?.First(a => a.Size == "extralarge").Text
                                    : null,
                                NowPlaying = s.AttributesLfm != null && s.AttributesLfm.Nowplaying,
                                TimePlayed = s.Date?.Uts != null
                                    ? DateTime.UnixEpoch.AddSeconds(s.Date.Uts).ToUniversalTime()
                                    : null
                            }).ToList()
                    },
                    Success = true
                };

                this._cache.Set(cacheKey, response, TimeSpan.FromSeconds(14));

                return response;
            }

            return new Response<RecentTrackList>
            {
                Success = false,
                Error = recentTracksCall.Error,
                Message = recentTracksCall.Message
            };
        }

        // Scrobble count from a certain unix timestamp
        public async Task<long?> GetScrobbleCountFromDateAsync(string lastFmUserName, long? unixTimestamp = null)
        {
            var queryParams = new Dictionary<string, string>
            {
                {"user", lastFmUserName },
                {"limit", "1"}
            };

            if (unixTimestamp != null)
            {
                queryParams.Add("from", unixTimestamp.ToString());
            }

            var recentTracksCall = await this._lastFmApi.CallApiAsync<RecentTracksListLfmResponseModel>(queryParams, Call.RecentTracks);

            return recentTracksCall.Success ? recentTracksCall.Content.RecentTracks.AttributesLfm.Total : (long?)null;
        }

        public static string TrackToLinkedString(RecentTrack track)
        {
            var escapedTrackName = Regex.Replace(track.TrackName, @"([|\\*])", @"\$1");

            if (!string.IsNullOrWhiteSpace(track.AlbumName))
            {
                var albumQueryName = track.AlbumName.Replace(" - Single", "");
                albumQueryName = albumQueryName.Replace(" - EP", "");

                var escapedAlbumName = Regex.Replace(track.AlbumName, @"([|\\*])", @"\$1");
                var albumRymUrl = @"https://duckduckgo.com/?q=%5Csite%3Arateyourmusic.com";
                albumRymUrl += HttpUtility.UrlEncode($" \"{albumQueryName}\" \"{track.ArtistName}\"");

                return $"[{escapedTrackName}]({track.TrackUrl})\n" +
                       $"By **{track.ArtistName}**" +
                       $" | *[{escapedAlbumName}]({albumRymUrl})*\n";
            }

            return $"[{escapedTrackName}]({track.TrackUrl})\n" +
                   $"By **{track.ArtistName}**\n";
        }

        public static string TrackToString(RecentTrack track)
        {
            return $"{track.TrackName}\n" +
                   $"By **{track.ArtistName}**" +
                   (string.IsNullOrWhiteSpace(track.AlbumName)
                       ? "\n"
                       : $" | *{track.AlbumName}*\n");
        }

        public static string ResponseTrackToLinkedString(TrackInfoLfm trackInfo)
        {
            if (trackInfo.Url.IndexOfAny(new[] { '(', ')' }) >= 0)
            {
                return ResponseTrackToString(trackInfo);
            }

            return $"[{trackInfo.Name}]({trackInfo.Url})\n" +
                   $"By **{trackInfo.Artist.Name}**" +
                   (trackInfo.Album == null || string.IsNullOrWhiteSpace(trackInfo.Album.Title)
                       ? "\n"
                       : $" | *{trackInfo.Album.Title}*\n");
        }

        private static string ResponseTrackToString(TrackInfoLfm trackInfo)
        {
            return $"{trackInfo.Name}\n" +
                   $"By **{trackInfo.Artist.Name}**" +
                   (trackInfo.Album == null || string.IsNullOrWhiteSpace(trackInfo.Album.Title)
                       ? "\n"
                       : $" | *{trackInfo.Album.Title}*\n");
        }

        public static string TrackToOneLinedString(RecentTrack trackLfm)
        {
            return $"**{trackLfm.TrackName}** by **{trackLfm.ArtistName}**";
        }

        public static string TagsToLinkedString(Tags tags)
        {
            var tagString = new StringBuilder();
            for (var i = 0; i < tags.Tag.Length; i++)
            {
                if (i != 0)
                {
                    tagString.Append(" - ");
                }
                var tag = tags.Tag[i];
                tagString.Append($"[{tag.Name}]({tag.Url})");
            }

            return tagString.ToString();
        }

        public static string TopTagsToString(TrackInfoTopTagsLfm tags)
        {
            var tagString = new StringBuilder();
            for (var i = 0; i < tags.Tag.Length; i++)
            {
                if (i != 0)
                {
                    tagString.Append(" - ");
                }
                var tag = tags.Tag[i];
                tagString.Append($"{tag.Name}");
            }

            return tagString.ToString();
        }

        // User
        public async Task<LastResponse<LastUser>> GetUserInfoAsync(string lastFmUserName)
        {
            var user = await this._lastFmClient.User.GetInfoAsync(lastFmUserName);
            Statistics.LastfmApiCalls.Inc();

            return user;
        }

        // User
        public async Task<UserLfm> GetFullUserInfoAsync(string lastFmUserName)
        {
            var queryParams = new Dictionary<string, string>
            {
                {"user", lastFmUserName }
            };

            var userCall = await this._lastFmApi.CallApiAsync<UserResponseLfm>(queryParams, Call.UserInfo);

            return !userCall.Success ? null : userCall.Content.User;
        }

        public async Task<PageResponse<LastTrack>> SearchTrackAsync(string searchQuery)
        {
            var trackSearch = await this._lastFmClient.Track.SearchAsync(searchQuery, itemsPerPage: 1);
            Statistics.LastfmApiCalls.Inc();

            return trackSearch;
        }

        // Track info
        public async Task<TrackInfoLfm> GetTrackInfoAsync(string trackName, string artistName, string username = null)
        {
            var queryParams = new Dictionary<string, string>
            {
                {"artist", artistName },
                {"track", trackName },
                {"username", username },
                {"autocorrect", "1"}
            };

            var trackCall = await this._lastFmApi.CallApiAsync<TrackInfoLfmResponse>(queryParams, Call.TrackInfo);

            return !trackCall.Success ? null : trackCall.Content.Track;
        }

        public async Task<Response<ArtistInfoLfmResponse>> GetArtistInfoAsync(string artistName, string username = null)
        {
            var queryParams = new Dictionary<string, string>
            {
                {"artist", artistName },
                {"username", username },
                {"autocorrect", "1"}
            };

            var artistCall = await this._lastFmApi.CallApiAsync<ArtistInfoLfmResponse>(queryParams, Call.ArtistInfo);

            return artistCall;
        }

        public async Task<Response<AlbumInfoLfmResponse>> GetAlbumInfoAsync(string artistName, string albumName, string username = null)
        {
            var queryParams = new Dictionary<string, string>
            {
                {"artist", artistName },
                {"album", albumName },
                {"username", username }
            };

            var albumCall = await this._lastFmApi.CallApiAsync<AlbumInfoLfmResponse>(queryParams, Call.AlbumInfo);

            return albumCall;
        }

        public async Task<PageResponse<LastAlbum>> SearchAlbumAsync(string searchQuery)
        {
            var albumSearch = await this._lastFmClient.Album.SearchAsync(searchQuery, itemsPerPage: 1);
            Statistics.LastfmApiCalls.Inc();

            return albumSearch;
        }

        // Album images
        public async Task<LastImageSet> GetAlbumImagesAsync(string artistName, string albumName)
        {
            var album = await this._lastFmClient.Album.GetInfoAsync(artistName, albumName);
            Statistics.LastfmApiCalls.Inc();

            return album?.Content?.Images;
        }

        public static async Task<Bitmap> GetAlbumImageAsBitmapAsync(Uri largestImageSize)
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
        public async Task<PageResponse<LastAlbum>> GetTopAlbumsAsync(string lastFmUserName, LastStatsTimeSpan timespan, int count = 2)
        {
            var topAlbums = await this._lastFmClient.User.GetTopAlbums(lastFmUserName, timespan, 1, count);
            Statistics.LastfmApiCalls.Inc();

            return topAlbums;
        }


        // Top artists
        public async Task<PageResponse<LastArtist>> GetTopArtistsAsync(string lastFmUserName,
            LastStatsTimeSpan timespan, int count = 2)
        {
            var topArtists = await this._lastFmClient.User.GetTopArtists(lastFmUserName, timespan, 1, count);
            Statistics.LastfmApiCalls.Inc();

            return topArtists;
        }

        // Top tracks
        public async Task<Response<TopTracksLfmResponse>> GetTopTracksAsync(string lastFmUserName,
            string period, int count = 2, int amountOfPages = 1)
        {
            var queryParams = new Dictionary<string, string>
            {
                {"limit", count.ToString() },
                {"username", lastFmUserName },
                {"period", period },
            };

            if (amountOfPages == 1)
            {
                return await this._lastFmApi.CallApiAsync<TopTracksLfmResponse>(queryParams, Call.TopTracks);
            }

            var response = await this._lastFmApi.CallApiAsync<TopTracksLfmResponse>(queryParams, Call.TopTracks);
            if (response.Success && response.Content.TopTracks.Track.Count > 998)
            {
                for (var i = 1; i < amountOfPages; i++)
                {
                    queryParams.Remove("page");
                    queryParams.Add("page", (i + 1).ToString());

                    var pageResponse = await this._lastFmApi.CallApiAsync<TopTracksLfmResponse>(queryParams, Call.TopTracks);

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

        // Check if Last.fm user exists
        public async Task<bool> LastFmUserExistsAsync(string lastFmUserName)
        {
            var lastFmUser = await this._lastFmClient.User.GetInfoAsync(lastFmUserName);
            Statistics.LastfmApiCalls.Inc();

            return lastFmUser.Success;
        }

        public async Task<Response<TokenResponse>> GetAuthToken()
        {
            var queryParams = new Dictionary<string, string>();

            var tokenCall = await this._lastFmApi.CallApiAsync<TokenResponse>(queryParams, Call.GetToken);

            return tokenCall;
        }

        public async Task<Response<AuthSessionResponse>> GetAuthSession(string token)
        {
            var queryParams = new Dictionary<string, string>
            {
                {"token", token}
            };

            var authSessionCall = await this._lastFmApi.CallApiAsync<AuthSessionResponse>(queryParams, Call.GetAuthSession, true);

            return authSessionCall;
        }

        public async Task<bool> LoveTrackAsync(User user, string artistName, string trackName)
        {
            var queryParams = new Dictionary<string, string>
            {
                {"artist", artistName},
                {"track", trackName},
                {"sk", user.SessionKeyLastFm}
            };

            var authSessionCall = await this._lastFmApi.CallApiAsync<AuthSessionResponse>(queryParams, Call.TrackLove, true);

            return authSessionCall.Success;
        }

        public async Task<bool> UnLoveTrackAsync(User user, string artistName, string trackName)
        {
            var queryParams = new Dictionary<string, string>
            {
                {"artist", artistName},
                {"track", trackName},
                {"sk", user.SessionKeyLastFm}
            };

            var authSessionCall = await this._lastFmApi.CallApiAsync<AuthSessionResponse>(queryParams, Call.TrackUnLove, true);

            return authSessionCall.Success;
        }

        public async Task<Response<ScrobbledTrack>> ScrobbleAsync(User user, string artistName, string trackName, string albumName = null)
        {
            var queryParams = new Dictionary<string, string>
            {
                {"artist", artistName},
                {"track", trackName},
                {"sk", user.SessionKeyLastFm},
                {"timestamp",  ((DateTimeOffset)DateTime.UtcNow).ToUnixTimeSeconds().ToString() }
            };

            if (!string.IsNullOrWhiteSpace(albumName))
            {
                queryParams.Add("album", albumName);
            }

            var authSessionCall = await this._lastFmApi.CallApiAsync<ScrobbledTrack>(queryParams, Call.TrackScrobble, true);

            return authSessionCall;
        }
    }
}
