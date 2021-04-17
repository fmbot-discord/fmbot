using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
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
using Tag = FMBot.Domain.Models.Tag;

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
                {"limit", count.ToString()},
                {"extended", "1" }
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
                        RecentTracks = recentTracksCall.Content.RecentTracks.Track.Select(LastfmTrackToRecentTrack).ToList()
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

        public async Task<Response<RecentTrackList>> GetLovedTracksAsync(string lastFmUserName, int count = 2, string sessionKey = null, long? fromUnixTimestamp = null)
        {
            var queryParams = new Dictionary<string, string>
            {
                {"limit", count.ToString() },
                {"username", lastFmUserName },
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

            var recentTracksCall = await this._lastFmApi.CallApiAsync<LovedTracksListLfmResponseModel>(queryParams, Call.LovedTracks, authorizedCall);

            if (recentTracksCall.Success)
            {
                var response = new Response<RecentTrackList>
                {
                    Content = new RecentTrackList
                    {
                        TotalAmount = recentTracksCall.Content.LovedTracks.AttributesLfm.Total,
                        UserUrl = $"https://www.last.fm/user/{lastFmUserName}",
                        UserRecentTracksUrl = $"https://www.last.fm/user/{lastFmUserName}/loved",
                        RecentTracks = recentTracksCall.Content.LovedTracks.Track.Select(LastfmTrackToRecentTrack).ToList()
                    },
                    Success = true
                };

                return response;
            }

            return new Response<RecentTrackList>
            {
                Success = false,
                Error = recentTracksCall.Error,
                Message = recentTracksCall.Message
            };
        }

        private RecentTrack LastfmTrackToRecentTrack(RecentTrackLfm recentTrackLfm)
        {
            return new()
            {
                TrackName = recentTrackLfm.Name,
                TrackUrl = recentTrackLfm.Url.ToString(),
                Loved = recentTrackLfm.Loved == "1",
                ArtistName = recentTrackLfm.Artist.Text,
                ArtistUrl = recentTrackLfm.Artist.Url,
                AlbumName = !string.IsNullOrWhiteSpace(recentTrackLfm.Album?.Text) ? recentTrackLfm.Album.Text : null,
                AlbumUrl = recentTrackLfm.Album?.Text,
                AlbumCoverUrl = recentTrackLfm.Image?.FirstOrDefault(a => a.Size == "extralarge") != null &&
                                !recentTrackLfm.Image.First(a => a.Size == "extralarge").Text
                                    .Contains(Constants.LastFmNonExistentImageName)
                    ? recentTrackLfm.Image?.First(a => a.Size == "extralarge").Text
                    : null,
                NowPlaying = recentTrackLfm.AttributesLfm != null && recentTrackLfm.AttributesLfm.Nowplaying,
                TimePlayed = recentTrackLfm.Date?.Uts != null
                    ? DateTime.UnixEpoch.AddSeconds(recentTrackLfm.Date.Uts).ToUniversalTime()
                    : null
            };
        }

        // Scrobble count from a certain unix timestamp
        public async Task<long?> GetScrobbleCountFromDateAsync(string lastFmUserName, long? unixTimestamp = null)
        {
            var queryParams = new Dictionary<string, string>
            {
                {"user", lastFmUserName },
                {"limit", "1"},
                {"extended", "1" }
            };

            if (unixTimestamp != null)
            {
                queryParams.Add("from", unixTimestamp.ToString());
            }

            var recentTracksCall = await this._lastFmApi.CallApiAsync<RecentTracksListLfmResponseModel>(queryParams, Call.RecentTracks);

            return recentTracksCall.Success ? recentTracksCall.Content.RecentTracks.AttributesLfm.Total : (long?)null;
        }

        // Scrobble count from a certain unix timestamp
        public async Task<Response<RecentTrack>> GetMilestoneScrobbleAsync(string lastFmUserName, string sessionKey, long totalScrobbles, long milestoneScrobble)
        {
            var queryParams = new Dictionary<string, string>
            {
                {"user", lastFmUserName },
                {"limit", "1"},
                {"extended", "1" }
            };

            var authorizedCall = false;

            if (!string.IsNullOrEmpty(sessionKey))
            {
                queryParams.Add("sk", sessionKey);
                authorizedCall = true;
            }

            var pageNumber = totalScrobbles - milestoneScrobble + 1;

            queryParams.Add("page", pageNumber.ToString());

            var recentTracksCall = await this._lastFmApi.CallApiAsync<RecentTracksListLfmResponseModel>(queryParams, Call.RecentTracks, authorizedCall);

            if (recentTracksCall.Success && recentTracksCall.Content.RecentTracks.Track.Any(w => w.AttributesLfm == null || !w.AttributesLfm.Nowplaying))
            {
                var response = new Response<RecentTrack>
                {
                    Content = LastfmTrackToRecentTrack(recentTracksCall.Content.RecentTracks.Track.First(w => w.AttributesLfm == null || !w.AttributesLfm.Nowplaying)),
                    Success = true
                };

                return response;
            }

            return new Response<RecentTrack>
            {
                Success = false,
                Error = recentTracksCall.Error,
                Message = recentTracksCall.Message
            };
        }

        public static string ResponseTrackToLinkedString(TrackInfo trackInfo)
        {
            if (trackInfo.TrackUrl.IndexOfAny(new[] { '(', ')' }) >= 0)
            {
                return ResponseTrackToString(trackInfo);
            }

            return $"[{trackInfo.TrackName}]({trackInfo.TrackUrl})\n" +
                   $"By **{trackInfo.ArtistName}**" +
                   (string.IsNullOrWhiteSpace(trackInfo.AlbumName)
                       ? "\n"
                       : $" | *{trackInfo.AlbumName}*\n");
        }

        private static string ResponseTrackToString(TrackInfo trackInfo)
        {
            return $"{trackInfo.TrackName}\n" +
                   $"By **{trackInfo.ArtistName}**" +
                   (string.IsNullOrWhiteSpace(trackInfo.AlbumName)
                       ? "\n"
                       : $" | *{trackInfo.AlbumName}*\n");
        }

        public static string TrackToOneLinedString(RecentTrack trackLfm)
        {
            return $"**{trackLfm.TrackName}** by **{trackLfm.ArtistName}**";
        }

        public static string TrackToOneLinedLinkedString(RecentTrack trackLfm)
        {
            return $"**[{trackLfm.TrackName}]({trackLfm.TrackUrl})** by **{trackLfm.ArtistName}**";
        }

        public static string TagsToLinkedString(TagsLfm tagsLfm)
        {
            var tagString = new StringBuilder();
            for (var i = 0; i < tagsLfm.Tag.Length; i++)
            {
                if (i != 0)
                {
                    tagString.Append(" - ");
                }
                var tag = tagsLfm.Tag[i];
                tagString.Append($"[{tag.Name}]({tag.Url})");
            }

            return tagString.ToString();
        }

        public static string TagsToLinkedString(List<Tag> tags)
        {
            var tagString = new StringBuilder();
            for (var i = 0; i < tags.Count; i++)
            {
                if (i != 0)
                {
                    tagString.Append(" - ");
                }
                var tag = tags[i];
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
        public async Task<UserLfm> GetFullUserInfoAsync(string lastFmUserName, string sessionKey)
        {
            var queryParams = new Dictionary<string, string>
            {
                {"user", lastFmUserName }
            };

            var authorizedCall = false;

            if (!string.IsNullOrEmpty(sessionKey))
            {
                queryParams.Add("sk", sessionKey);
                authorizedCall = true;
            }

            var userCall = await this._lastFmApi.CallApiAsync<UserResponseLfm>(queryParams, Call.UserInfo, authorizedCall);

            return !userCall.Success ? null : userCall.Content.User;
        }

        public async Task<PageResponse<LastTrack>> SearchTrackAsync(string searchQuery)
        {
            var trackSearch = await this._lastFmClient.Track.SearchAsync(searchQuery, itemsPerPage: 1);
            Statistics.LastfmApiCalls.Inc();

            return trackSearch;
        }

        // Track info
        public async Task<Response<TrackInfo>> GetTrackInfoAsync(string trackName, string artistName, string username = null)
        {
            var queryParams = new Dictionary<string, string>
            {
                {"artist", artistName },
                {"track", trackName },
                {"autocorrect", "1"},
                {"extended", "1" }
            };

            if (username != null)
            {
                queryParams.Add("username", username);
            }

            var trackCall = await this._lastFmApi.CallApiAsync<TrackInfoLfmResponse>(queryParams, Call.TrackInfo);
            if (trackCall.Success)
            {
                var linkToFilter = $"<a href=\"{trackCall.Content.Track.Url.Replace("https", "http")}\">Read more on Last.fm</a>";
                var filteredSummary = trackCall.Content.Track.Wiki?.Summary.Replace(linkToFilter, "");

                return new Response<TrackInfo>
                {
                    Success = true,
                    Content = new TrackInfo
                    {
                        TrackName = trackCall.Content.Track.Name,
                        TrackUrl = Uri.IsWellFormedUriString(trackCall.Content.Track.Url, UriKind.Absolute)
                            ? trackCall.Content.Track.Url
                            : null,
                        AlbumName = trackCall.Content.Track.Album?.Title,
                        AlbumArtist = trackCall.Content.Track.Album?.Artist,
                        AlbumUrl = trackCall.Content.Track.Album?.Url,
                        ArtistName = trackCall.Content.Track.Artist?.Name,
                        ArtistUrl = trackCall.Content.Track.Artist?.Name,
                        ArtistMbid = !string.IsNullOrWhiteSpace(trackCall.Content.Track.Artist?.Mbid)
                            ? Guid.Parse(trackCall.Content.Track.Artist?.Mbid)
                            : null,
                        Mbid = !string.IsNullOrWhiteSpace(trackCall.Content.Track.Mbid)
                            ? Guid.Parse(trackCall.Content.Track.Mbid)
                            : null,
                        Description = filteredSummary,
                        TotalPlaycount = trackCall.Content.Track.Playcount,
                        TotalListeners = trackCall.Content.Track.Listeners,
                        Duration = trackCall.Content.Track.Duration,
                        UserPlaycount = trackCall.Content.Track.Userplaycount,
                        Loved = trackCall.Content.Track.Userloved == "1",
                        Tags = trackCall.Content.Track.Toptags.Tag.Select(s => new Tag
                        {
                            Name = s.Name,
                            Url = s.Url
                        }).ToList(),
                    }
                };
            }

            return new Response<TrackInfo>
            {
                Success = false,
                Error = trackCall.Error,
                Message = trackCall.Message
            };

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
            var lastFmUser = await this.GetFullUserInfoAsync(lastFmUserName, null);

            return lastFmUser != null;
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

        public async Task<Response<ScrobbledTrack>> SetNowPlayingAsync(User user, string artistName, string trackName, string albumName = null)
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

            var authSessionCall = await this._lastFmApi.CallApiAsync<ScrobbledTrack>(queryParams, Call.TrackUpdateNowPlaying, true);

            return authSessionCall;
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
