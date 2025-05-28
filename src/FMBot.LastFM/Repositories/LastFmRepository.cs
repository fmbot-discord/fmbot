using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using FMBot.Domain;
using FMBot.Domain.Enums;
using FMBot.Domain.Extensions;
using FMBot.Domain.Interfaces;
using FMBot.Domain.Models;
using FMBot.Domain.Types;
using FMBot.LastFM.Api;
using FMBot.LastFM.Models;
using FMBot.LastFM.Types;
using FMBot.Persistence.Domain.Models;
using IF.Lastfm.Core.Api;
using IF.Lastfm.Core.Api.Enums;
using IF.Lastfm.Core.Api.Helpers;
using IF.Lastfm.Core.Objects;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Serilog;
using Tag = FMBot.Domain.Models.Tag;
using TimePeriod = FMBot.Domain.Models.TimePeriod;

namespace FMBot.LastFM.Repositories;

public class LastFmRepository : ILastfmRepository
{
    private readonly LastfmClient _lastFmClient;
    private readonly IMemoryCache _cache;
    private readonly ILastfmApi _lastFmApi;
    private readonly HttpClient _client;

    public LastFmRepository(IConfiguration configuration, ILastfmApi lastFmApi, IMemoryCache cache,
        HttpClient httpClient)
    {
        this._lastFmClient =
            new LastfmClient(configuration.GetSection("LastFm:PrivateKey").Value,
                configuration.GetSection("LastFm:PrivateKeySecret").Value, httpClient);
        this._lastFmApi = lastFmApi;
        this._cache = cache;
        this._client = httpClient;
    }

    // Recent scrobbles
    public async Task<Response<RecentTrackList>> GetRecentTracksAsync(string lastFmUserName, int count = 2,
        bool useCache = false, string sessionKey = null, long? fromUnixTimestamp = null, int amountOfPages = 1)
    {
        var cacheKey = $"{lastFmUserName}-lastfm-recent-tracks";
        var queryParams = new Dictionary<string, string>
        {
            { "user", lastFmUserName },
            { "limit", count.ToString() },
            { "extended", "1" }
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
            if (cachedRecentTracks && recentTrackResponse.RecentTracks.Count != 0 &&
                recentTrackResponse.RecentTracks.Count >= count)
            {
                return new Response<RecentTrackList>
                {
                    Content = recentTrackResponse,
                    Success = true
                };
            }
        }

        try
        {
            var recentTracksCall = await this._lastFmApi.CallApiAsync<RecentTracksListLfmResponseModel>(queryParams,
                Call.RecentTracks, authorizedCall);

            if (amountOfPages > 1 && recentTracksCall.Success &&
                recentTracksCall.Content.RecentTracks.Track.Count >= (count - 2) && count >= 400)
            {
                for (var i = 1; i < amountOfPages; i++)
                {
                    queryParams.Remove("page");
                    queryParams.Add("page", (i + 1).ToString());
                    var pageResponse =
                        await this._lastFmApi.CallApiAsync<RecentTracksListLfmResponseModel>(queryParams,
                            Call.RecentTracks, authorizedCall);

                    if (pageResponse.Success)
                    {
                        recentTracksCall.Content.RecentTracks.Track.AddRange(
                            pageResponse.Content.RecentTracks.Track);
                        if (pageResponse.Content.RecentTracks.Track.Count < 1000)
                        {
                            break;
                        }
                    }
                    else if (pageResponse.Error == ResponseStatus.Failure)
                    {
                        pageResponse =
                            await this._lastFmApi.CallApiAsync<RecentTracksListLfmResponseModel>(queryParams,
                                Call.RecentTracks, authorizedCall);
                        if (pageResponse.Success)
                        {
                            recentTracksCall.Content.RecentTracks.Track.AddRange(pageResponse.Content.RecentTracks
                                .Track);
                            if (pageResponse.Content.RecentTracks.Track.Count < 1000)
                            {
                                break;
                            }
                        }
                        else
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

            if (recentTracksCall.Success)
            {
                var response = new Response<RecentTrackList>
                {
                    Content = new RecentTrackList
                    {
                        TotalAmount = recentTracksCall.Content.RecentTracks.AttributesLfm.Total,
                        UserUrl = $"https://www.last.fm/user/{lastFmUserName}",
                        UserRecentTracksUrl = $"https://www.last.fm/user/{lastFmUserName}/library",
                        RecentTracks = recentTracksCall.Content.RecentTracks.Track.Select(LastfmTrackToRecentTrack)
                            .ToList()
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
        catch (Exception e)
        {
            Log.Error("LastFmRepository: Error in GetRecentTracksAsync for {lastFmUserName}", lastFmUserName, e);
            throw;
        }
    }

    public async Task<Response<RecentTrackList>> GetLovedTracksAsync(string lastFmUserName, int count = 2,
        string sessionKey = null, long? fromUnixTimestamp = null)
    {
        var queryParams = new Dictionary<string, string>
        {
            { "limit", count.ToString() },
            { "username", lastFmUserName },
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

        var recentTracksCall =
            await this._lastFmApi.CallApiAsync<LovedTracksListLfmResponseModel>(queryParams, Call.LovedTracks,
                authorizedCall);

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

    private static RecentTrack LastfmTrackToRecentTrack(RecentTrackLfm recentTrackLfm)
    {
        return new()
        {
            TrackName = recentTrackLfm.Name,
            TrackUrl = recentTrackLfm.Url.ToString(),
            Loved = recentTrackLfm.Loved == "1",
            ArtistName = recentTrackLfm.Artist.Text,
            ArtistUrl = recentTrackLfm.Artist.Url,
            AlbumName = !string.IsNullOrWhiteSpace(recentTrackLfm.Album?.Text) ? recentTrackLfm.Album.Text : null,
            AlbumCoverUrl = recentTrackLfm.Image?.FirstOrDefault(a => a.Size == "extralarge") != null &&
                            !string.IsNullOrWhiteSpace(recentTrackLfm.Image.First(a => a.Size == "extralarge").Text) &&
                            !recentTrackLfm.Image.First(a => a.Size == "extralarge").Text
                                .Contains(Constants.LastFmNonExistentImageName)
                ? recentTrackLfm.Image?.First(a => a.Size == "extralarge").Text.Replace("/u/300x300/", "/u/")
                : null,
            NowPlaying = recentTrackLfm.AttributesLfm != null && recentTrackLfm.AttributesLfm.Nowplaying,
            TimePlayed = recentTrackLfm.Date?.Uts != null
                ? DateTime.UnixEpoch.AddSeconds(recentTrackLfm.Date.Uts).ToUniversalTime()
                : null
        };
    }

// Scrobble count from a certain unix timestamp
    public async Task<long?> GetScrobbleCountFromDateAsync(string lastFmUserName, long? from = null,
        string sessionKey = null, long? until = null)
    {
        var queryParams = new Dictionary<string, string>
        {
            { "user", lastFmUserName },
            { "limit", "1" },
            { "extended", "1" }
        };

        var authorizedCall = false;

        if (!string.IsNullOrEmpty(sessionKey))
        {
            queryParams.Add("sk", sessionKey);
            authorizedCall = true;
        }

        if (from != null)
        {
            queryParams.Add("from", from.ToString());
        }

        if (until != null)
        {
            queryParams.Add("to", until.ToString());
        }

        var recentTracksCall =
            await this._lastFmApi.CallApiAsync<RecentTracksListLfmResponseModel>(queryParams, Call.RecentTracks,
                authorizedCall);

        return recentTracksCall.Success ? recentTracksCall.Content.RecentTracks.AttributesLfm.Total : (long?)null;
    }

// Scrobble count from a certain unix timestamp
    public async Task<Response<RecentTrack>> GetMilestoneScrobbleAsync(string lastFmUserName, string sessionKey,
        long totalScrobbles, long milestoneScrobble)
    {
        var queryParams = new Dictionary<string, string>
        {
            { "user", lastFmUserName },
            { "limit", "1" },
            { "extended", "1" }
        };

        var authorizedCall = false;

        if (!string.IsNullOrEmpty(sessionKey))
        {
            queryParams.Add("sk", sessionKey);
            authorizedCall = true;
        }

        var pageNumber = totalScrobbles - milestoneScrobble + 1;

        queryParams.Add("page", pageNumber.ToString());

        var recentTracksCall =
            await this._lastFmApi.CallApiAsync<RecentTracksListLfmResponseModel>(queryParams, Call.RecentTracks,
                authorizedCall);

        if (recentTracksCall.Success &&
            recentTracksCall.Content.RecentTracks.Track.Any(w =>
                w.AttributesLfm == null || !w.AttributesLfm.Nowplaying))
        {
            var response = new Response<RecentTrack>
            {
                Content = LastfmTrackToRecentTrack(
                    recentTracksCall.Content.RecentTracks.Track.First(w =>
                        w.AttributesLfm == null || !w.AttributesLfm.Nowplaying)),
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
        if (string.IsNullOrEmpty(trackInfo.TrackUrl) || trackInfo.TrackUrl.IndexOfAny(new[] { '(', ')' }) >= 0)
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

// User
    public async Task<DataSourceUser> GetLfmUserInfoAsync(string lastFmUserName)
    {
        var queryParams = new Dictionary<string, string>
        {
            { "user", lastFmUserName }
        };

        var userCall = await this._lastFmApi.CallApiAsync<UserResponseLfm>(queryParams, Call.UserInfo);

        return !userCall.Success
            ? null
            : new DataSourceUser
            {
                Playcount = userCall.Content.User.Playcount,
                AlbumCount = userCall.Content.User.AlbumCount,
                ArtistCount = userCall.Content.User.ArtistCount,
                TrackCount = userCall.Content.User.TrackCount,
                Name = userCall.Content.User.Realname,
                Country = userCall.Content.User.Country,
                Url = userCall.Content.User.Url.ToString(),
                Registered = DateTime.UnixEpoch.AddSeconds(userCall.Content.User.Registered.Unixtime).ToUniversalTime(),
                RegisteredUnix = userCall.Content.User.Registered.Unixtime,
                LfmRegisteredUnix = userCall.Content.User.Registered.Unixtime,
                Image = userCall.Content.User.Image.FirstOrDefault(a => a.Size == "extralarge") != null &&
                        !string.IsNullOrWhiteSpace(userCall.Content.User.Image.First(a => a.Size == "extralarge").Text)
                    ? userCall.Content.User.Image?.First(a => a.Size == "extralarge").Text.Replace("/u/300x300/", "/u/")
                    : null,
                Type = userCall.Content.User.Type,
                Subscriber = userCall.Content.User.Subscriber == 1
            };
    }

    public async Task<Response<TrackInfo>> SearchTrackAsync(string searchQuery)
    {
        var trackSearch = await this._lastFmClient.Track.SearchAsync(searchQuery, itemsPerPage: 10);
        Statistics.LastfmApiCalls.WithLabels("track.search").Inc();

        if (!trackSearch.Success)
        {
            return new Response<TrackInfo>
            {
                Success = false,
                Error = Enum.Parse<ResponseStatus>(trackSearch.Status.ToString()),
                Message = "Last.fm returned an error"
            };
        }

        if (trackSearch.Content == null || trackSearch.TotalItems == 0)
        {
            return new Response<TrackInfo>
            {
                Success = true,
                Content = null,
            };
        }

        var firstValidTrack = trackSearch.Content
            .FirstOrDefault(track => !IsArtistBlocked(track.ArtistName));

        if (firstValidTrack == null)
        {
            return new Response<TrackInfo>
            {
                Success = true,
                Content = null
            };
        }

        return new Response<TrackInfo>
        {
            Success = true,
            Content = new TrackInfo
            {
                ArtistName = firstValidTrack.ArtistName,
                AlbumArtist = firstValidTrack.ArtistName,
                AlbumName = firstValidTrack.AlbumName,
                TrackName = firstValidTrack.Name
            }
        };
    }

    public async Task<Response<TrackInfo>> GetTrackInfoAsync(string trackName, string artistName, bool redirectsEnabled,
        string username = null)
    {
        var queryParams = new Dictionary<string, string>
        {
            { "artist", artistName },
            { "track", trackName },
            { "autocorrect", redirectsEnabled ? "1" : "0" },
            { "extended", "1" }
        };

        if (username != null)
        {
            queryParams.Add("username", username);
        }

        var trackCall = await this._lastFmApi.CallApiAsync<TrackInfoLfmResponse>(queryParams, Call.TrackInfo);
        if (trackCall.Success)
        {
            var linkToFilter = $"<a href=\"{trackCall.Content.Track.Url}\">Read more on Last.fm</a>";
            var filteredSummary = trackCall.Content.Track.Wiki?.Summary.Replace(linkToFilter, "")
                .Replace(linkToFilter.Replace("https", "http"), "");

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
                    ArtistUrl = Uri.IsWellFormedUriString(trackCall.Content.Track.Artist?.Url, UriKind.Absolute)
                        ? trackCall.Content.Track.Artist?.Url
                        : null,
                    ArtistMbid = !string.IsNullOrWhiteSpace(trackCall.Content.Track.Artist?.Mbid)
                        ? Guid.Parse(trackCall.Content.Track.Artist?.Mbid)
                        : null,
                    Mbid = !string.IsNullOrWhiteSpace(trackCall.Content.Track.Mbid)
                        ? Guid.Parse(trackCall.Content.Track.Mbid)
                        : null,
                    Description = !string.IsNullOrWhiteSpace(filteredSummary)
                        ? filteredSummary.Replace(". .", ".")
                        : null,
                    TotalPlaycount = trackCall.Content.Track.Playcount ?? 0,
                    TotalListeners = trackCall.Content.Track.Listeners ?? 0,
                    Duration = trackCall.Content.Track.Duration != 0 ? trackCall.Content.Track.Duration : (long?)null,
                    UserPlaycount = trackCall.Content.Track.Userplaycount,
                    Loved = trackCall.Content.Track.Userloved == "1",
                    Tags = trackCall.Content.Track.Toptags?.Tag?.Select(s => new Tag
                    {
                        Name = s.Name,
                        Url = s.Url
                    }).ToList()
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

    public async Task<Response<ArtistInfo>> GetArtistInfoAsync(string artistName, string username,
        bool redirectsEnabled)
    {
        var queryParams = new Dictionary<string, string>
        {
            { "artist", artistName },
            { "username", username },
            { "autocorrect", redirectsEnabled ? "1" : "0" }
        };

        var artistCall = await this._lastFmApi.CallApiAsync<ArtistInfoLfmResponse>(queryParams, Call.ArtistInfo);

        if (artistCall.Success)
        {
            var linkToFilter = $"<a href=\"{artistCall.Content.Artist.Url}\">Read more on Last.fm</a>";
            var filteredSummary = artistCall.Content.Artist.Bio?.Summary.Replace(linkToFilter, "");

            return new Response<ArtistInfo>
            {
                Success = true,
                Content = new ArtistInfo
                {
                    ArtistName = artistCall.Content.Artist.Name,
                    ArtistUrl = Uri.IsWellFormedUriString(artistCall.Content.Artist.Url, UriKind.Absolute)
                        ? artistCall.Content.Artist.Url
                        : null,
                    Mbid = !string.IsNullOrWhiteSpace(artistCall.Content.Artist.Mbid)
                        ? Guid.Parse(artistCall.Content.Artist.Mbid)
                        : null,
                    Description = !string.IsNullOrWhiteSpace(filteredSummary)
                        ? filteredSummary.Replace(". .", ".").Replace("\n\n", "\n")
                        : null,
                    TotalPlaycount = artistCall.Content.Artist.Stats?.Playcount ?? 0,
                    TotalListeners = artistCall.Content.Artist.Stats?.Listeners ?? 0,
                    UserPlaycount = artistCall.Content.Artist.Stats?.Userplaycount,
                    Tags = artistCall.Content.Artist.Tags?.Tag?.Select(s => new Tag
                    {
                        Name = s.Name,
                        Url = s.Url
                    }).ToList()
                }
            };
        }

        return new Response<ArtistInfo>
        {
            Success = false,
            Error = artistCall.Error,
            Message = artistCall.Message
        };
    }

    public async Task<Response<AlbumInfo>> GetAlbumInfoAsync(string artistName, string albumName, bool redirectsEnabled,
        string username = null)
    {
        var queryParams = new Dictionary<string, string>
        {
            { "artist", artistName },
            { "album", albumName },
            { "autocorrect", redirectsEnabled ? "1" : "0" }
        };

        if (!string.IsNullOrEmpty(username))
        {
            queryParams.Add("username", username);
        }

        var albumCall = await this._lastFmApi.CallApiAsync<AlbumInfoLfmResponse>(queryParams, Call.AlbumInfo);
        if (albumCall.Success)
        {
            var linkToFilter = $"<a href=\"{albumCall.Content.Album.Url}\">Read more on Last.fm</a>";
            var filteredSummary = albumCall.Content.Album.Wiki?.Summary.Replace(linkToFilter, "").Replace(". .", ".");

            return new Response<AlbumInfo>
            {
                Success = true,
                Content = new AlbumInfo
                {
                    AlbumName = albumCall.Content.Album.Name,
                    AlbumUrl = Uri.IsWellFormedUriString(albumCall.Content.Album.Url.Trim(), UriKind.Absolute)
                        ? albumCall.Content.Album.Url.Trim()
                        : LastfmUrlExtensions.GetAlbumUrl(albumCall.Content.Album.Artist, albumCall.Content.Album.Name),
                    ArtistName = albumCall.Content.Album.Artist,
                    ArtistUrl = LastfmUrlExtensions.GetArtistUrl(albumCall.Content.Album.Artist),
                    Mbid = !string.IsNullOrWhiteSpace(albumCall.Content.Album.Mbid)
                        ? Guid.Parse(albumCall.Content.Album.Mbid)
                        : null,
                    Description = !string.IsNullOrWhiteSpace(filteredSummary)
                        ? filteredSummary
                        : null,
                    TotalPlaycount = albumCall.Content.Album.Playcount ?? 0,
                    TotalListeners = albumCall.Content.Album.Listeners ?? 0,
                    TotalDuration = albumCall.Content.Album.Tracks?.Track?.Sum(s => s.Duration),
                    UserPlaycount = albumCall.Content.Album.Userplaycount,
                    AlbumTracks = albumCall.Content.Album.Tracks?.Track?.Select(s => new AlbumTrack
                    {
                        ArtistName = s.Artist?.Name,
                        TrackName = s.Name,
                        TrackUrl = s.Url,
                        DurationSeconds = s.Duration,
                        Rank = s.Attr?.Rank
                    }).ToList(),
                    Tags = albumCall.Content.Album.Tags?.Tag?.Select(s => new Tag
                    {
                        Name = s.Name,
                        Url = s.Url
                    }).ToList(),
                    AlbumCoverUrl = !string.IsNullOrWhiteSpace(albumCall.Content.Album.Image
                                        ?.FirstOrDefault(a => a.Size == "extralarge")?.Text) &&
                                    !albumCall.Content.Album.Image.First(a => a.Size == "extralarge").Text
                                        .Contains(Constants.LastFmNonExistentImageName)
                        ? albumCall.Content.Album.Image?.First(a => a.Size == "extralarge").Text
                            .Replace("/u/300x300/", "/u/")
                        : null,
                }
            };
        }

        return new Response<AlbumInfo>
        {
            Success = false,
            Error = albumCall.Error,
            Message = albumCall.Message
        };
    }

    public async Task<Response<AlbumInfo>> SearchAlbumAsync(string searchQuery)
    {
        var albumSearch = await this._lastFmClient.Album.SearchAsync(searchQuery, itemsPerPage: 10);
        Statistics.LastfmApiCalls.WithLabels("album.search").Inc();

        if (!albumSearch.Success)
        {
            return new Response<AlbumInfo>
            {
                Success = false,
                Error = Enum.Parse<ResponseStatus>(albumSearch.Status.ToString()),
                Message = "Last.fm returned an error"
            };
        }

        if (!albumSearch.Content.Any())
        {
            return new Response<AlbumInfo>
            {
                Success = true,
                Content = null
            };
        }

        var firstValidAlbum = albumSearch.Content
            .FirstOrDefault(album => !IsArtistBlocked(album.ArtistName));

        if (firstValidAlbum == null)
        {
            return new Response<AlbumInfo>
            {
                Success = true,
                Content = null
            };
        }

        return new Response<AlbumInfo>
        {
            Success = true,
            Content = new AlbumInfo
            {
                AlbumName = firstValidAlbum.Name,
                AlbumUrl = firstValidAlbum.Url.ToString(),
                ArtistName = firstValidAlbum.ArtistName
            }
        };
    }

    public async Task<MemoryStream> GetAlbumImageAsStreamAsync(string imageUrl)
    {
        try
        {
            await using var file = await this._client.GetStreamAsync(imageUrl);
            var memoryStream = new MemoryStream();

            await file.CopyToAsync(memoryStream);

            memoryStream.Position = 0;
            return memoryStream;
        }
        catch
        {
            return null;
        }
    }

    public async Task<Response<TopAlbumList>> GetTopAlbumsAsync(string lastFmUserName,
        TimeSettingsModel timeSettings, int count = 2, int amountOfPages = 1, bool useCache = false)
    {
        try
        {
            var cacheKey =
                $"{lastFmUserName}-lastfm-top-albums-{timeSettings.Description}-{timeSettings.UrlParameter}";
            if (useCache)
            {
                var cachedTopAlbums = this._cache.TryGetValue(cacheKey, out TopAlbumList topAlbumResponse);
                if (cachedTopAlbums && topAlbumResponse.TopAlbums.Any() &&
                    topAlbumResponse.TopAlbums.Count >= count)
                {
                    return new Response<TopAlbumList>
                    {
                        Content = topAlbumResponse,
                        Success = true
                    };
                }
            }

            Response<TopAlbumList> response;
            if (!timeSettings.UseCustomTimePeriod || !timeSettings.StartDateTime.HasValue ||
                !timeSettings.EndDateTime.HasValue || timeSettings.TimePeriod == TimePeriod.AllTime)
            {
                response = await GetTopAlbumsAsync(lastFmUserName, timeSettings.TimePeriod, count, amountOfPages);
            }
            else
            {
                response = await GetTopAlbumsForCustomTimePeriodAsyncAsync(lastFmUserName,
                    timeSettings.StartDateTime.Value,
                    timeSettings.EndDateTime.Value, (int)count);
            }

            if (response.Success && amountOfPages == 1)
            {
                this._cache.Set(cacheKey, response.Content, TimeSpan.FromMinutes(2));
            }

            return response;
        }
        catch (Exception e)
        {
            Log.Error("LastFmRepository: Error in GetTopAlbumsAsync for {lastFmUserName}", lastFmUserName, e);
            return new Response<TopAlbumList>
            {
                Success = false,
                Error = ResponseStatus.Unknown,
                Message = ".fmbot error while deserializing Last.fm response"
            };
        }
    }

// Top albums
    public async Task<Response<TopAlbumList>> GetTopAlbumsAsync(string lastFmUserName,
        TimePeriod timePeriod, int count = 2, int amountOfPages = 1)
    {
        var lastStatsTimeSpan = TimePeriodToLastStatsTimeSpan(timePeriod);

        var albums = new List<LastAlbum>();
        PageResponse<LastAlbum> topAlbums;

        if (amountOfPages == 1)
        {
            topAlbums = await this._lastFmClient.User.GetTopAlbums(lastFmUserName, lastStatsTimeSpan, 1, count);
            Statistics.LastfmApiCalls.WithLabels("user.getTopAlbums").Inc();

            if (topAlbums.Success && topAlbums.Content.Any())
            {
                albums.AddRange(topAlbums.Content);
            }
        }
        else
        {
            topAlbums = await this._lastFmClient.User.GetTopAlbums(lastFmUserName, lastStatsTimeSpan, 1, count);
            Statistics.LastfmApiCalls.WithLabels("user.getTopAlbums").Inc();

            if (topAlbums.Success && topAlbums.Content != null)
            {
                albums.AddRange(topAlbums.Content);

                if (topAlbums.Content.Count > 998)
                {
                    for (var i = 2; i <= amountOfPages; i++)
                    {
                        topAlbums = await this._lastFmClient.User.GetTopAlbums(lastFmUserName, lastStatsTimeSpan, i,
                            count);
                        Statistics.LastfmApiCalls.WithLabels("user.getTopAlbums").Inc();

                        if (!topAlbums.Success)
                        {
                            break;
                        }

                        if (topAlbums.Content.Any())
                        {
                            albums.AddRange(topAlbums.Content);
                            if (topAlbums.Count() < 1000)
                            {
                                break;
                            }
                        }
                    }
                }
            }
        }


        if (!topAlbums.Success)
        {
            return new Response<TopAlbumList>
            {
                Success = false,
                Error = (ResponseStatus)Enum.Parse(typeof(ResponseStatus), topAlbums.Status.ToString()),
                Message = "Last.fm returned an error"
            };
        }

        if (topAlbums.Content == null || !albums.Any())
        {
            return new Response<TopAlbumList>
            {
                Success = true,
                Content = new TopAlbumList()
            };
        }

        return new Response<TopAlbumList>
        {
            Success = true,
            Content = new TopAlbumList
            {
                TotalAmount = topAlbums.TotalItems,
                TopAlbums = albums.Select(s => new TopAlbum
                {
                    ArtistName = s.ArtistName,
                    AlbumName = s.Name,
                    AlbumCoverUrl = !string.IsNullOrWhiteSpace(s.Images?.ExtraLarge?.ToString()) &&
                                    !s.Images.ExtraLarge.AbsoluteUri.Contains(Constants.LastFmNonExistentImageName)
                        ? s.Images?.ExtraLarge.ToString().Replace("/u/300x300/", "/u/")
                        : null,
                    AlbumUrl = s.Url.ToString(),
                    UserPlaycount = s.PlayCount,
                    Mbid = !string.IsNullOrWhiteSpace(s.Mbid)
                        ? Guid.Parse(s.Mbid)
                        : null
                }).ToList()
            }
        };
    }

    public async Task<Response<TopAlbumList>> GetTopAlbumsForCustomTimePeriodAsyncAsync(string lastFmUserName,
        DateTime startDateTime, DateTime endDateTime, int count)
    {
        var start = ((DateTimeOffset)startDateTime).ToUnixTimeSeconds();
        var end = ((DateTimeOffset)endDateTime).ToUnixTimeSeconds();
        var queryParams = new Dictionary<string, string>
        {
            { "username", lastFmUserName },
            { "limit", count.ToString() },
            { "from", start.ToString() },
            { "to", end.ToString() },
        };

        var artistCall =
            await this._lastFmApi.CallApiAsync<WeeklyAlbumChartsResponse>(queryParams, Call.GetWeeklyAlbumChart);
        if (artistCall.Success)
        {
            return new Response<TopAlbumList>
            {
                Success = true,
                Content = new TopAlbumList
                {
                    TopAlbums = artistCall.Content.WeeklyAlbumChart.Album
                        .OrderByDescending(o => o.Playcount)
                        .Select(s => new TopAlbum
                        {
                            ArtistName = s.Artist.Text,
                            AlbumName = s.Name,
                            Mbid = !string.IsNullOrWhiteSpace(s.Mbid)
                                ? Guid.Parse(s.Mbid)
                                : null,
                            AlbumUrl = s.Url,
                            UserPlaycount = s.Playcount
                        }).ToList()
                }
            };
        }

        return new Response<TopAlbumList>
        {
            Success = false,
            Error = artistCall.Error,
            Message = artistCall.Message
        };
    }

    public async Task<Response<TopArtistList>> GetTopArtistsAsync(string lastFmUserName,
        TimeSettingsModel timeSettings, long count = 2, long amountOfPages = 1, bool useCache = false)
    {
        try
        {
            var cacheKey =
                $"{lastFmUserName}-lastfm-top-artists-{timeSettings.Description}-{timeSettings.UrlParameter}";
            if (useCache)
            {
                var cachedTopArtists = this._cache.TryGetValue(cacheKey, out TopArtistList topArtistResponse);
                if (cachedTopArtists && topArtistResponse.TopArtists.Any() &&
                    topArtistResponse.TopArtists.Count >= count)
                {
                    return new Response<TopArtistList>
                    {
                        Content = topArtistResponse,
                        Success = true
                    };
                }
            }

            Response<TopArtistList> response;
            if (!timeSettings.UseCustomTimePeriod || !timeSettings.StartDateTime.HasValue ||
                !timeSettings.EndDateTime.HasValue || timeSettings.TimePeriod == TimePeriod.AllTime)
            {
                response = await GetTopArtistsAsync(lastFmUserName, timeSettings.TimePeriod, count, amountOfPages);
            }
            else
            {
                response = await GetTopArtistsForCustomTimePeriodAsync(lastFmUserName, timeSettings.StartDateTime.Value,
                    timeSettings.EndDateTime.Value, (int)count);
            }

            if (response.Success && amountOfPages == 1)
            {
                this._cache.Set(cacheKey, response.Content, TimeSpan.FromMinutes(2));
            }

            return response;
        }
        catch (Exception e)
        {
            Log.Error("LastFmRepository: Error in GetTopArtistsAsync for {lastFmUserName}", lastFmUserName, e);
            return new Response<TopArtistList>
            {
                Success = false,
                Error = ResponseStatus.Unknown,
                Message = ".fmbot error while deserializing Last.fm response"
            };
        }
    }

// Top artists
    public async Task<Response<TopArtistList>> GetTopArtistsAsync(string lastFmUserName,
        TimePeriod timePeriod, long count = 2, long amountOfPages = 1)
    {
        var lastStatsTimeSpan = TimePeriodToLastStatsTimeSpan(timePeriod);

        var artists = new List<LastArtist>();
        PageResponse<LastArtist> topArtists;

        if (amountOfPages == 1)
        {
            topArtists = await this._lastFmClient.User.GetTopArtists(lastFmUserName, lastStatsTimeSpan, 1, (int)count);
            Statistics.LastfmApiCalls.WithLabels("user.getTopArtists").Inc();

            if (topArtists.Success && topArtists.Content.Any())
            {
                artists.AddRange(topArtists.Content);
            }
        }
        else
        {
            topArtists = await this._lastFmClient.User.GetTopArtists(lastFmUserName, lastStatsTimeSpan, 1, (int)count);
            Statistics.LastfmApiCalls.WithLabels("user.getTopArtists").Inc();

            if (topArtists.Success)
            {
                artists.AddRange(topArtists.Content);

                if (topArtists.Content.Count > 998)
                {
                    for (var i = 2; i <= amountOfPages; i++)
                    {
                        topArtists =
                            await this._lastFmClient.User.GetTopArtists(lastFmUserName, lastStatsTimeSpan, i,
                                (int)count);
                        Statistics.LastfmApiCalls.WithLabels("user.getTopArtists").Inc();

                        if (!topArtists.Success)
                        {
                            break;
                        }

                        if (topArtists.Content.Any())
                        {
                            artists.AddRange(topArtists.Content);
                            if (topArtists.Count() < 1000)
                            {
                                break;
                            }
                        }
                    }
                }
            }
        }

        if (!topArtists.Success)
        {
            return new Response<TopArtistList>
            {
                Success = false,
                Error = (ResponseStatus)Enum.Parse(typeof(ResponseStatus), topArtists.Status.ToString()),
                Message = "Last.fm returned an error"
            };
        }

        if (topArtists.Content == null || topArtists.TotalItems == 0)
        {
            return new Response<TopArtistList>
            {
                Success = true,
                Content = new TopArtistList()
            };
        }

        return new Response<TopArtistList>
        {
            Success = true,
            Content = new TopArtistList
            {
                TotalAmount = topArtists.TotalItems,
                TopArtists = artists.Select(s => new TopArtist
                {
                    ArtistName = s.Name,
                    ArtistUrl = s.Url.ToString(),
                    UserPlaycount = s.PlayCount.GetValueOrDefault(),
                    Mbid = !string.IsNullOrWhiteSpace(s.Mbid)
                        ? Guid.Parse(s.Mbid)
                        : null
                }).ToList()
            }
        };
    }

// Top artists custom time period
    public async Task<Response<TopArtistList>> GetTopArtistsForCustomTimePeriodAsync(string lastFmUserName,
        DateTime startDateTime, DateTime endDateTime, int count)
    {
        var start = ((DateTimeOffset)startDateTime).ToUnixTimeSeconds();
        var end = ((DateTimeOffset)endDateTime).ToUnixTimeSeconds();

        var queryParams = new Dictionary<string, string>
        {
            { "username", lastFmUserName },
            { "limit", count.ToString() },
            { "from", start.ToString() },
            { "to", end.ToString() },
        };

        var artistCall =
            await this._lastFmApi.CallApiAsync<WeeklyArtistChartsResponse>(queryParams, Call.GetWeeklyArtistChart);
        if (artistCall.Success)
        {
            return new Response<TopArtistList>
            {
                Success = true,
                Content = new TopArtistList
                {
                    TopArtists = artistCall.Content.WeeklyArtistChart.Artist
                        .OrderByDescending(o => o.Playcount)
                        .Select(s => new TopArtist
                        {
                            ArtistUrl = s.Url,
                            ArtistName = s.Name,
                            Mbid = !string.IsNullOrWhiteSpace(s.Mbid)
                                ? Guid.Parse(s.Mbid)
                                : null,
                            UserPlaycount = s.Playcount
                        }).ToList()
                }
            };
        }

        return new Response<TopArtistList>
        {
            Success = false,
            Error = artistCall.Error,
            Message = artistCall.Message
        };
    }

    public async Task<Response<TopTrackList>> GetTopTracksAsync(string lastFmUserName,
        TimeSettingsModel timeSettings, int count = 2, int amountOfPages = 1)
    {
        try
        {
            if (!timeSettings.UseCustomTimePeriod || !timeSettings.StartDateTime.HasValue ||
                !timeSettings.EndDateTime.HasValue || timeSettings.TimePeriod == TimePeriod.AllTime)
            {
                return await GetTopTracksAsync(lastFmUserName, timeSettings.ApiParameter, count, amountOfPages);
            }

            return await GetTopTracksForCustomTimePeriodAsyncAsync(lastFmUserName, timeSettings.StartDateTime.Value,
                timeSettings.EndDateTime.Value, count);
        }
        catch (Exception e)
        {
            Log.Error("LastFmRepository: Error in GetTopTracksAsync for {lastFmUserName}", lastFmUserName, e);
            return new Response<TopTrackList>
            {
                Success = false,
                Error = ResponseStatus.Unknown,
                Message = ".fmbot error while deserializing Last.fm response"
            };
        }
    }

// Top tracks
    public async Task<Response<TopTrackList>> GetTopTracksAsync(string lastFmUserName,
        string period, int count = 2, int amountOfPages = 1)
    {
        var queryParams = new Dictionary<string, string>
        {
            { "limit", count.ToString() },
            { "username", lastFmUserName },
            { "period", period },
        };

        Response<TopTracksLfmResponse> topTracksCall;
        if (amountOfPages == 1)
        {
            topTracksCall = await this._lastFmApi.CallApiAsync<TopTracksLfmResponse>(queryParams, Call.TopTracks);
        }
        else
        {
            topTracksCall = await this._lastFmApi.CallApiAsync<TopTracksLfmResponse>(queryParams, Call.TopTracks);
            if (topTracksCall.Success && topTracksCall.Content.TopTracks.Track.Count > 998)
            {
                for (var i = 2; i <= amountOfPages; i++)
                {
                    queryParams.Remove("page");
                    queryParams.Add("page", i.ToString());
                    var pageResponse =
                        await this._lastFmApi.CallApiAsync<TopTracksLfmResponse>(queryParams, Call.TopTracks);

                    if (pageResponse.Success)
                    {
                        topTracksCall.Content.TopTracks.Track.AddRange(pageResponse.Content.TopTracks.Track);
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
        }

        if (topTracksCall.Success)
        {
            return new Response<TopTrackList>
            {
                Success = true,
                Content = new TopTrackList
                {
                    TotalAmount = topTracksCall.Content.TopTracks.Attr?.Total,
                    TopTracks = topTracksCall.Content.TopTracks.Track.Select(s => new TopTrack
                    {
                        AlbumCoverUrl = !string.IsNullOrWhiteSpace(s.Image
                            ?.FirstOrDefault(f => f.Size != null && f.Size.ToLower() == "extralarge")?.Text)
                            ? s.Image.First(f => f.Size.ToLower() == "extralarge").Text
                            : null,
                        TrackName = s.Name,
                        ArtistName = s.Artist.Name,
                        Mbid = !string.IsNullOrWhiteSpace(s.Mbid)
                            ? Guid.Parse(s.Mbid)
                            : null,
                        TrackUrl = Uri.IsWellFormedUriString(s.Url, UriKind.Absolute)
                            ? s.Url
                            : null,
                        UserPlaycount = s.Playcount
                    }).ToList()
                }
            };
        }

        return new Response<TopTrackList>
        {
            Success = false,
            Error = topTracksCall.Error,
            Message = topTracksCall.Message
        };
    }

    public async Task<Response<TopTrackList>> GetTopTracksForCustomTimePeriodAsyncAsync(string lastFmUserName,
        DateTime startDateTime, DateTime endDateTime, int count)
    {
        var start = ((DateTimeOffset)startDateTime).ToUnixTimeSeconds();
        var end = ((DateTimeOffset)endDateTime).ToUnixTimeSeconds();
        var queryParams = new Dictionary<string, string>
        {
            { "username", lastFmUserName },
            { "limit", count.ToString() },
            { "from", start.ToString() },
            { "to", end.ToString() },
        };

        var artistCall =
            await this._lastFmApi.CallApiAsync<WeeklyTrackChartsResponse>(queryParams, Call.GetWeeklyTrackChart);
        if (artistCall.Success)
        {
            return new Response<TopTrackList>
            {
                Success = true,
                Content = new TopTrackList
                {
                    TopTracks = artistCall.Content.WeeklyTrackChart.Track
                        .OrderByDescending(o => o.Playcount)
                        .Select(s => new TopTrack
                        {
                            ArtistUrl = s.Url,
                            ArtistName = s.Artist.Text,
                            TrackName = s.Name,
                            Mbid = !string.IsNullOrWhiteSpace(s.Mbid)
                                ? Guid.Parse(s.Mbid)
                                : null,
                            TrackUrl = Uri.IsWellFormedUriString(s.Url, UriKind.Absolute)
                                ? s.Url
                                : null,
                            UserPlaycount = s.Playcount
                        }).ToList()
                }
            };
        }

        return new Response<TopTrackList>
        {
            Success = false,
            Error = artistCall.Error,
            Message = artistCall.Message
        };
    }

    private LastStatsTimeSpan TimePeriodToLastStatsTimeSpan(TimePeriod timePeriod)
    {
        return timePeriod switch
        {
            TimePeriod.Weekly => LastStatsTimeSpan.Week,
            TimePeriod.Monthly => LastStatsTimeSpan.Month,
            TimePeriod.Yearly => LastStatsTimeSpan.Year,
            TimePeriod.AllTime => LastStatsTimeSpan.Overall,
            TimePeriod.Quarterly => LastStatsTimeSpan.Quarter,
            TimePeriod.Half => LastStatsTimeSpan.Half,
            _ => throw new ArgumentOutOfRangeException(nameof(timePeriod), timePeriod, null)
        };
    }

// Check if Last.fm user exists
    public async Task<bool> LastFmUserExistsAsync(string lastFmUserName, bool alsoExistsIfPrivate = false)
    {
        var dateFromFilter = DateTime.UtcNow.AddDays(-365);
        var timeFrom = (long?)((DateTimeOffset)dateFromFilter).ToUnixTimeSeconds();

        var scrobbles = await this.GetRecentTracksAsync(lastFmUserName, fromUnixTimestamp: timeFrom);

        if (alsoExistsIfPrivate)
        {
            return scrobbles.Success && scrobbles.Content.RecentTracks.Count > 0 ||
                   scrobbles.Error == ResponseStatus.LoginRequired;
        }

        return scrobbles.Success && scrobbles.Content.RecentTracks.Count > 0;
    }

    public async Task<Response<TokenResponse>> GetAuthToken()
    {
        var queryParams = new Dictionary<string, string>();

        var tokenCall =
            await this._lastFmApi.CallApiAsync<TokenResponse>(queryParams, Call.GetToken, usePrivateKey: true);

        return tokenCall;
    }

    public async Task<Response<AuthSessionResponse>> GetAuthSession(string token)
    {
        var queryParams = new Dictionary<string, string>
        {
            { "token", token }
        };

        var authSessionCall =
            await this._lastFmApi.CallApiAsync<AuthSessionResponse>(queryParams, Call.GetAuthSession, true);

        return authSessionCall;
    }

    public async Task<bool> LoveTrackAsync(string lastFmSessionKey, string artistName, string trackName)
    {
        var queryParams = new Dictionary<string, string>
        {
            { "artist", artistName },
            { "track", trackName },
            { "sk", lastFmSessionKey }
        };

        var loveCall = await this._lastFmApi.CallApiAsync<AuthSessionResponse>(queryParams, Call.TrackLove, true);

        return loveCall.Success;
    }

    public async Task<bool> UnLoveTrackAsync(string lastFmSessionKey, string artistName, string trackName)
    {
        var queryParams = new Dictionary<string, string>
        {
            { "artist", artistName },
            { "track", trackName },
            { "sk", lastFmSessionKey }
        };

        var unLoveCall = await this._lastFmApi.CallApiAsync<AuthSessionResponse>(queryParams, Call.TrackUnLove, true);

        return unLoveCall.Success;
    }

    public async Task<Response<bool>> SetNowPlayingAsync(string lastFmSessionKey, string artistName, string trackName,
        string albumName = null)
    {
        var queryParams = new Dictionary<string, string>
        {
            { "artist", artistName },
            { "track", trackName },
            { "sk", lastFmSessionKey },
            { "timestamp", ((DateTimeOffset)DateTime.UtcNow).ToUnixTimeSeconds().ToString() }
        };

        if (!string.IsNullOrWhiteSpace(albumName))
        {
            queryParams.Add("album", albumName);
        }

        var nowPlayingCall =
            await this._lastFmApi.CallApiAsync<NowPlayingLfmResponse>(queryParams, Call.TrackUpdateNowPlaying, true);

        return new Response<bool>
        {
            Success = nowPlayingCall.Success,
            Content = nowPlayingCall.Content.Nowplaying.IgnoredMessage.Code == 0
        };
    }

    public async Task<Response<StoredPlayResponse>> ScrobbleAsync(string lastFmSessionKey, string artistName,
        string trackName, string albumName = null, DateTime? timeStamp = null)
    {
        timeStamp ??= DateTime.UtcNow;

        var queryParams = new Dictionary<string, string>
        {
            { "artist", artistName },
            { "track", trackName },
            { "sk", lastFmSessionKey },
            { "timestamp", ((DateTimeOffset)timeStamp).ToUnixTimeSeconds().ToString() }
        };

        if (!string.IsNullOrWhiteSpace(albumName))
        {
            queryParams.Add("album", albumName);
        }

        var scrobbleCall = await this._lastFmApi.CallApiAsync<ScrobbledTrack>(queryParams, Call.TrackScrobble, true);

        if (scrobbleCall.Success)
        {
            return new Response<StoredPlayResponse>
            {
                Success = true,
                Content = new StoredPlayResponse
                {
                    Accepted = scrobbleCall.Content.Scrobbles.Attr.Accepted > 0,
                    Ignored = scrobbleCall.Content.Scrobbles.Attr.Ignored > 0,
                    IgnoreMessage = scrobbleCall.Content.Scrobbles.Scrobble?.IgnoredMessage?.Text
                }
            };
        }

        return new Response<StoredPlayResponse>
        {
            Success = false,
            Error = scrobbleCall.Error,
            Message = scrobbleCall.Message
        };
    }

    private static bool IsArtistBlocked(string artistName) =>
        !string.IsNullOrWhiteSpace(artistName) && SearchBlockedArtists.Contains(artistName);

    private static readonly HashSet<string> SearchBlockedArtists = new(StringComparer.OrdinalIgnoreCase)
    {
        "pornhub.com"
    };
}
