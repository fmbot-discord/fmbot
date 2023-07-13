using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Dapper;
using Discord;
using FMBot.Bot.Extensions;
using FMBot.Bot.Interfaces;
using FMBot.Bot.Models;
using FMBot.Bot.Services.ThirdParty;
using FMBot.Bot.Services.WhoKnows;
using FMBot.Domain;
using FMBot.Domain.Enums;
using FMBot.Domain.Interfaces;
using FMBot.Domain.Models;
using FMBot.Domain.Types;
using FMBot.LastFM.Domain.Types;
using FMBot.LastFM.Repositories;
using FMBot.Persistence.Domain.Models;
using FMBot.Persistence.EntityFrameWork;
using FMBot.Persistence.Repositories;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Npgsql;
using Serilog;

namespace FMBot.Bot.Services;

public class TrackService
{
    private readonly IDataSourceFactory _dataSourceFactory;
    private readonly SpotifyService _spotifyService;
    private readonly HttpClient _client;
    private readonly BotSettings _botSettings;
    private readonly IMemoryCache _cache;
    private readonly TrackRepository _trackRepository;
    private readonly IDbContextFactory<FMBotDbContext> _contextFactory;
    private readonly TimerService _timer;
    private readonly AlbumService _albumService;
    private readonly WhoKnowsTrackService _whoKnowsTrackService;
    private readonly ArtistsService _artistsService;
    private readonly IUpdateService _updateService;

    public TrackService(HttpClient httpClient,
        IDataSourceFactory dataSourceFactory,
        IOptions<BotSettings> botSettings,
        SpotifyService spotifyService,
        IMemoryCache memoryCache,
        TrackRepository trackRepository,
        IDbContextFactory<FMBotDbContext> contextFactory,
        TimerService timer,
        AlbumService albumService,
        WhoKnowsTrackService whoKnowsTrackService,
        ArtistsService artistsService,
        IUpdateService updateService)
    {
        this._dataSourceFactory = dataSourceFactory;
        this._spotifyService = spotifyService;
        this._cache = memoryCache;
        this._trackRepository = trackRepository;
        this._client = httpClient;
        this._botSettings = botSettings.Value;
        this._contextFactory = contextFactory;
        this._timer = timer;
        this._albumService = albumService;
        this._whoKnowsTrackService = whoKnowsTrackService;
        this._artistsService = artistsService;
        this._updateService = updateService;
    }

    public async Task<TrackSearch> SearchTrack(ResponseModel response, IUser discordUser, string trackValues,
        string lastFmUserName, string sessionKey = null,
        string otherUserUsername = null, bool useCachedTracks = false, int? userId = null)
    {
        string searchValue;
        if (!string.IsNullOrWhiteSpace(trackValues) && trackValues.Length != 0)
        {
            searchValue = trackValues;

            if (searchValue.ToLower() == "featured" && this._timer.CurrentFeatured.TrackName != null)
            {
                searchValue =
                    $"{this._timer.CurrentFeatured.ArtistName} | {this._timer.CurrentFeatured.TrackName}";
            }

            if (searchValue.Contains(" | "))
            {
                if (otherUserUsername != null)
                {
                    lastFmUserName = otherUserUsername;
                }

                var trackName = searchValue.Split(" | ")[1];
                var trackArtist = searchValue.Split(" | ")[0];

                Response<TrackInfo> trackInfo;
                if (useCachedTracks)
                {
                    trackInfo = await GetCachedTrack(trackArtist, trackName, lastFmUserName, userId);
                }
                else
                {
                    trackInfo = await this._dataSourceFactory.GetTrackInfoAsync(trackName, trackArtist,
                        lastFmUserName);
                }

                if (!trackInfo.Success && trackInfo.Error == ResponseStatus.MissingParameters)
                {
                    response.Embed.WithDescription(
                        $"Track `{trackName}` by `{trackArtist}` could not be found, please check your search values and try again.");
                    response.CommandResponse = CommandResponse.NotFound;
                    response.ResponseType = ResponseType.Embed;
                    return new TrackSearch(null, response);
                }
                if (!trackInfo.Success || trackInfo.Content == null)
                {
                    response.Embed.ErrorResponse(trackInfo.Error, trackInfo.Message, null, discordUser, "album");
                    response.CommandResponse = CommandResponse.LastFmError;
                    response.ResponseType = ResponseType.Embed;
                    return new TrackSearch(null, response);
                }

                return new TrackSearch(trackInfo.Content, response);
            }
        }
        else
        {
            Response<RecentTrackList> recentScrobbles;

            if (userId.HasValue && otherUserUsername == null)
            {
                recentScrobbles = await this._updateService.UpdateUser(new UpdateUserQueueItem(userId.Value));
            }
            else
            {
                recentScrobbles = await this._dataSourceFactory.GetRecentTracksAsync(lastFmUserName, 1, true, sessionKey);
            }

            if (GenericEmbedService.RecentScrobbleCallFailed(recentScrobbles))
            {
                response.Embed =
                    GenericEmbedService.RecentScrobbleCallFailedBuilder(recentScrobbles, lastFmUserName);
                response.ResponseType = ResponseType.Embed;
                return new TrackSearch(null, response);
            }

            if (otherUserUsername != null)
            {
                lastFmUserName = otherUserUsername;
            }

            var lastPlayedTrack = recentScrobbles.Content.RecentTracks[0];

            Response<TrackInfo> trackInfo;
            if (useCachedTracks)
            {
                trackInfo = await GetCachedTrack(lastPlayedTrack.ArtistName, lastPlayedTrack.TrackName, lastFmUserName, userId);
            }
            else
            {
                trackInfo = await this._dataSourceFactory.GetTrackInfoAsync(lastPlayedTrack.TrackName, lastPlayedTrack.ArtistName,
                    lastFmUserName);
            }

            if (trackInfo?.Content == null || !trackInfo.Success)
            {
                response.Embed.WithDescription(
                    $"Last.fm did not return a result for **{lastPlayedTrack.TrackName}** by **{lastPlayedTrack.ArtistName}**.\n\n" +
                    $"This usually happens on recently released tracks. Please try again later.");

                response.CommandResponse = CommandResponse.NotFound;
                response.ResponseType = ResponseType.Embed;
                return new TrackSearch(null, response);
            }

            return new TrackSearch(trackInfo.Content, response);
        }

        var result = await this._dataSourceFactory.SearchTrackAsync(searchValue);
        if (result.Success && result.Content != null)
        {
            if (otherUserUsername != null)
            {
                lastFmUserName = otherUserUsername;
            }

            Response<TrackInfo> trackInfo;
            if (useCachedTracks)
            {
                trackInfo = await GetCachedTrack(result.Content.ArtistName, result.Content.TrackName, lastFmUserName, userId);
            }
            else
            {
                trackInfo = await this._dataSourceFactory.GetTrackInfoAsync(result.Content.TrackName, result.Content.ArtistName,
                    lastFmUserName);
            }

            if (trackInfo?.Content == null || !trackInfo.Success)
            {
                response.Embed.WithDescription(
                    $"Last.fm did not return a result for **{result.Content.TrackName}** by **{result.Content.ArtistName}**.\n\n" +
                    $"This usually happens on recently released tracks. Please try again later.");

                response.CommandResponse = CommandResponse.NotFound;
                response.ResponseType = ResponseType.Embed;
                return new TrackSearch(null, response);
            }

            return new TrackSearch(trackInfo.Content, response);
        }

        if (result.Success)
        {
            response.Embed.WithDescription($"Track could not be found, please check your search values and try again.");
            response.Embed.WithFooter($"Search value: '{searchValue}'");
            response.CommandResponse = CommandResponse.LastFmError;
            response.ResponseType = ResponseType.Embed;
            return new TrackSearch(null, response);
        }

        response.Embed.WithDescription($"Last.fm returned an error: {result.Error}");
        response.CommandResponse = CommandResponse.LastFmError;
        response.ResponseType = ResponseType.Embed;
        return new TrackSearch(null, response);
    }

    public async Task<Response<TrackInfo>> GetCachedTrack(string artistName, string trackName, string lastFmUserName, int? userId = null)
    {
        Response<TrackInfo> trackInfo;
        var cachedTrack = await GetTrackFromDatabase(artistName, trackName);
        if (cachedTrack != null)
        {
            trackInfo = new Response<TrackInfo>
            {
                Content = CachedTrackToTrackInfo(cachedTrack),
                Success = true
            };

            if (userId.HasValue)
            {
                var userPlaycount = await this._whoKnowsTrackService.GetTrackPlayCountForUser(cachedTrack.ArtistName,
                    cachedTrack.Name, userId.Value);
                trackInfo.Content.UserPlaycount = userPlaycount;
            }

            var cachedAlbum = await this._albumService.GetAlbumFromDatabase(cachedTrack.ArtistName, cachedTrack.AlbumName);
            if (cachedAlbum != null)
            {
                trackInfo.Content.AlbumCoverUrl = cachedAlbum.SpotifyImageUrl ?? cachedAlbum.SpotifyImageUrl;
                trackInfo.Content.AlbumUrl = cachedAlbum.LastFmUrl;
                trackInfo.Content.TrackUrl = cachedAlbum.LastFmUrl;
            }
        }
        else
        {
            trackInfo = await this._dataSourceFactory.GetTrackInfoAsync(trackName, artistName,
                lastFmUserName);
        }

        return trackInfo;
    }

    public async Task<TrackSearchResult> GetTrackFromLink(string description, bool possiblyContainsLinks = true, bool skipUploaderName = false)
    {
        try
        {
            if (description.ToLower().Contains("open.spotify.com/track/"))
            {
                var rx = new Regex(@"(https:\/\/open.spotify.com\/track\/|spotify:track:)([a-zA-Z0-9]+)(.*)$",
                    RegexOptions.Compiled | RegexOptions.IgnoreCase);

                // Find matches.
                var matches = rx.Matches(description);

                if (matches.Count > 0 && matches[0].Groups.Count > 1)
                {
                    var id = matches[0].Groups[2].ToString();
                    var spotifyResult = await this._spotifyService.GetTrackById(id);

                    if (spotifyResult != null)
                    {
                        return new TrackSearchResult
                        {
                            AlbumName = spotifyResult.Album.Name,
                            ArtistName = spotifyResult.Artists.First().Name,
                            TrackName = spotifyResult.Name
                        };
                    }
                }
            }

            if (description.Contains("[") && possiblyContainsLinks)
            {
                description = description.Split('[', ']')[1];
            }

            var trackAndArtist = ParseBoldDelimitedTrackAndArtist(description);
            if (trackAndArtist != null)
            {
                var track = await GetTrackFromDatabase(trackAndArtist.Artist, trackAndArtist.Track);

                if (track != null)
                {
                    return new TrackSearchResult
                    {
                        AlbumName = track.AlbumName,
                        ArtistName = track.ArtistName,
                        TrackName = track.Name
                    };
                }

                return new TrackSearchResult
                {
                    ArtistName = trackAndArtist.Artist,
                    TrackName = trackAndArtist.Track
                };
            }

            if (!description.Contains("-"))
            {
                var lastFmResult = await this._dataSourceFactory.SearchTrackAsync(description);
                if (lastFmResult.Success && lastFmResult.Content != null)
                {
                    return new TrackSearchResult
                    {
                        AlbumName = lastFmResult.Content.AlbumName,
                        ArtistName = lastFmResult.Content.ArtistName,
                        TrackName = lastFmResult.Content.TrackName
                    };
                }
            }
            else
            {
                if (description.Contains("~"))
                { // check whether requester is "on" in hydra
                    description = description.Split(" ~ ")[0];
                }

                var splitDesc = description.Split(" - ", 3);

                string artistName;
                string trackName;

                // Soundcloud uploader name is often different, so in case of 3 dashes skip the uploader name
                if (splitDesc.Length == 3 && skipUploaderName)
                {
                    artistName = splitDesc[1];
                    trackName = splitDesc[2];
                }
                else
                {
                    artistName = splitDesc[0];
                    trackName = splitDesc[1];
                }

                trackName = trackName.Replace("\\", "");

                var queryParams = new Dictionary<string, string>
                {
                    {"track", trackName }
                };

                var url = QueryHelpers.AddQueryString("https://metadata-filter.vercel.app/api/youtube", queryParams);

                var request = new HttpRequestMessage
                {
                    RequestUri = new Uri(url),
                    Method = HttpMethod.Post
                };

                using var httpResponse =
                    await this._client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

                if (httpResponse.IsSuccessStatusCode)
                {
                    var stream = await httpResponse.Content.ReadAsStreamAsync();
                    using var streamReader = new StreamReader(stream);
                    var requestBody = await streamReader.ReadToEndAsync();

                    var deserializeObject = JsonSerializer.Deserialize<CleanedUpResponse>(requestBody);
                    if (deserializeObject != null)
                    {
                        trackName = deserializeObject.data.track;
                    }
                }

                var trackLfm = await this._dataSourceFactory.GetTrackInfoAsync(trackName, artistName);

                if (trackLfm.Success)
                {
                    return new TrackSearchResult
                    {
                        TrackName = trackLfm.Content.TrackName,
                        AlbumName = trackLfm.Content.AlbumName,
                        ArtistName = trackLfm.Content.ArtistName
                    };
                }
            }

            return null;

        }
        catch (Exception e)
        {
            Log.Error("BotScrobbling: Error while getting track for description: {description}", description, e);
            return null;
        }
    }

    public class CleanedUpResponseTrack
    {
        public string track { get; set; }
    }

    public class CleanedUpResponse
    {
        public string status { get; set; }
        public CleanedUpResponseTrack data { get; set; }
    }

    internal static TrackAndArtist ParseBoldDelimitedTrackAndArtist(string description)
    {
        const string byDelimiter = " **by** ";
        var delimiterIndex = description.IndexOf(byDelimiter, 0, StringComparison.Ordinal);
        if (delimiterIndex != -1)
        {
            string UnBold(string s)
            {
                var split = s.Split("**");
                //**text** => text
                return split.Length == 3 ? split[1] : null;
            }
            var left = UnBold(description[..delimiterIndex]);
            var right = UnBold(description[(delimiterIndex + byDelimiter.Length)..]);
            if (left != null && right != null)
            {
                return new TrackAndArtist
                {
                    Track = left,
                    Artist = right
                };
            }
        }
        return null;
    }

    internal record TrackAndArtist
    {
        internal string Track { get; init; }
        internal string Artist { get; init; }

        public override string ToString()
        {
            return $"{nameof(this.Track)}: {this.Track}, {nameof(this.Artist)}: {this.Artist}";
        }
    }

    public async Task<List<UserTrack>> GetArtistUserTracks(int userId, string artistName)
    {
        const string sql = "SELECT user_track_id, user_id, name, artist_name, playcount" +
                           " FROM public.user_tracks where user_id = @userId AND UPPER(artist_name) = UPPER(CAST(@artistName AS CITEXT));";

        DefaultTypeMap.MatchNamesWithUnderscores = true;
        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        return (await connection.QueryAsync<UserTrack>(sql, new
        {
            userId,
            artistName
        })).ToList();
    }

    public record AudioFeaturesOverview(int Total, InternalTrackAudioFeatures Average);

    public async Task<AudioFeaturesOverview> GetAverageTrackAudioFeaturesForTopTracks(List<TopTrack> topTracks)
    {
        await CacheTrackAudioFeatures();
        var averageAudioFeatures = new InternalTrackAudioFeatures();

        if (topTracks == null || !topTracks.Any())
        {
            return new AudioFeaturesOverview(0, null);
        }

        var count = 0;
        foreach (var track in topTracks)
        {
            var audioFeatures = (InternalTrackAudioFeatures)this._cache.Get(CacheKeyForAudioFeature(track.TrackName, track.ArtistName));

            if (audioFeatures != null)
            {
                averageAudioFeatures.Danceability += audioFeatures.Danceability;
                averageAudioFeatures.Energy += audioFeatures.Energy;
                averageAudioFeatures.Tempo += audioFeatures.Tempo;
                averageAudioFeatures.Speechiness += audioFeatures.Speechiness;
                averageAudioFeatures.Acousticness += audioFeatures.Acousticness;
                averageAudioFeatures.Instrumentalness += audioFeatures.Instrumentalness;
                averageAudioFeatures.Valence += audioFeatures.Valence;
                averageAudioFeatures.Tempo += audioFeatures.Tempo;

                count++;
            }
        }

        return new AudioFeaturesOverview(count, averageAudioFeatures);
    }

    public static string AudioFeatureAnalysisComparisonString(AudioFeaturesOverview currentOverview,
        AudioFeaturesOverview previousOverview)
    {
        var trackAudioFeatureDescription = new StringBuilder();

        var avgCurrentDanceability = (decimal)currentOverview.Average.Danceability / currentOverview.Total;
        trackAudioFeatureDescription.Append($"**Danceability**: **__{avgCurrentDanceability:P}__**");
        if (previousOverview.Total > 0)
        {
            var avgPrevDanceability = (decimal)previousOverview.Average.Danceability / previousOverview.Total;
            trackAudioFeatureDescription.Append(
                $" ({StringExtensions.GetChangeString(avgPrevDanceability, avgCurrentDanceability)} {avgPrevDanceability:P})");
        }
        trackAudioFeatureDescription.AppendLine();

        var avgCurrentEnergy = (decimal)currentOverview.Average.Energy / currentOverview.Total;
        trackAudioFeatureDescription.Append($"**Energy**: **__{avgCurrentEnergy:P}__**");
        if (previousOverview.Total > 0)
        {
            var avgPrevEnergy = (decimal)previousOverview.Average.Energy / previousOverview.Total;
            trackAudioFeatureDescription.Append(
                $" ({StringExtensions.GetChangeString(avgPrevEnergy, avgCurrentEnergy)} {avgPrevEnergy:P})");
        }
        trackAudioFeatureDescription.AppendLine();

        var avgCurrentSpeechiness = (decimal)currentOverview.Average.Speechiness / currentOverview.Total;
        trackAudioFeatureDescription.Append($"**Speechiness**: **__{avgCurrentSpeechiness:P}__**");
        if (previousOverview.Total > 0)
        {
            var avgPrevSpeechiness = (decimal)previousOverview.Average.Speechiness / previousOverview.Total;
            trackAudioFeatureDescription.Append(
                $" ({StringExtensions.GetChangeString(avgPrevSpeechiness, avgCurrentSpeechiness)} {avgPrevSpeechiness:P})");
        }
        trackAudioFeatureDescription.AppendLine();

        var avgCurrentAcousticness = (decimal)currentOverview.Average.Acousticness / currentOverview.Total;
        trackAudioFeatureDescription.Append($"**Acousticness**: **__{avgCurrentAcousticness:P}__**");
        if (previousOverview.Total > 0)
        {
            var avgPrevAcousticness = (decimal)previousOverview.Average.Acousticness / previousOverview.Total;
            trackAudioFeatureDescription.Append(
                $" ({StringExtensions.GetChangeString(avgPrevAcousticness, avgCurrentAcousticness)} {avgPrevAcousticness:P})");
        }
        trackAudioFeatureDescription.AppendLine();

        var avgCurrentInstrumentalness = (decimal)currentOverview.Average.Instrumentalness / currentOverview.Total;
        trackAudioFeatureDescription.Append($"**Instrumentalness**: **__{avgCurrentInstrumentalness:P}__**");
        if (previousOverview.Total > 0)
        {
            var avgPrevInstrumentalness = (decimal)previousOverview.Average.Instrumentalness / previousOverview.Total;
            trackAudioFeatureDescription.Append(
                $" ({StringExtensions.GetChangeString(avgPrevInstrumentalness, avgCurrentInstrumentalness)} {avgPrevInstrumentalness:P})");
        }
        trackAudioFeatureDescription.AppendLine();

        var avgCurrentValence = (decimal)currentOverview.Average.Valence / currentOverview.Total;
        trackAudioFeatureDescription.Append($"**Musical positiveness**: **__{avgCurrentValence:P}__**");
        if (previousOverview.Total > 0)
        {
            var avgPrevValence = (decimal)previousOverview.Average.Valence / previousOverview.Total;
            trackAudioFeatureDescription.Append(
                $" ({StringExtensions.GetChangeString(avgPrevValence, avgCurrentValence)} {avgPrevValence:P})");
        }
        trackAudioFeatureDescription.AppendLine();

        //trackAudioFeatureDescription.Append(
        //    $"**Average tempo**: **__{currentOverview.Average.Tempo / currentOverview.Total:0.0} BPM**");
        //if (previousOverview.Total > 0)
        //{
        //    trackAudioFeatureDescription.Append(
        //        $" (from {previousOverview.Average.Tempo / previousOverview.Total:0.0} BPM)");
        //}
        //trackAudioFeatureDescription.AppendLine();

        return trackAudioFeatureDescription.ToString();
    }

    private async Task CacheTrackAudioFeatures()
    {
        const string cacheKey = "track-audio-features";
        var cacheTime = TimeSpan.FromMinutes(5);

        if (this._cache.TryGetValue(cacheKey, out _))
        {
            return;
        }

        const string sql = "SELECT * " +
                           "FROM public.tracks where valence IS NOT null and tempo IS NOT null;";

        DefaultTypeMap.MatchNamesWithUnderscores = true;
        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        var audioFeatures = (await connection.QueryAsync<Track>(sql)).ToList();

        foreach (var track in audioFeatures.Where(w => w.Valence.HasValue && w.Tempo.HasValue))
        {
            var audioFeature = new InternalTrackAudioFeatures(track.Danceability.GetValueOrDefault(),
                track.Energy.GetValueOrDefault(), track.Speechiness.GetValueOrDefault(), track.Acousticness.GetValueOrDefault(),
                track.Instrumentalness.GetValueOrDefault(), track.Valence.GetValueOrDefault(), (decimal)track.Tempo.GetValueOrDefault());

            this._cache.Set(CacheKeyForAudioFeature(track.Name, track.ArtistName), audioFeature);
        }

        this._cache.Set(cacheKey, true, cacheTime);
    }

    public static string CacheKeyForAudioFeature(string trackName, string artistName)
    {
        return $"audio-features-{trackName.ToLower()}-{artistName.ToLower()}";
    }

    public async Task<Track> GetTrackForId(int trackId)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();

        return await db.Tracks.FindAsync(trackId);
    }

    public async Task<Track> GetTrackFromDatabase(string artistName, string trackName)
    {
        if (string.IsNullOrWhiteSpace(artistName) || string.IsNullOrWhiteSpace(trackName))
        {
            return null;
        }

        var correctedArtistName = await this._artistsService.GetCorrectedArtistName(artistName);

        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        var album = await TrackRepository.GetTrackForName(correctedArtistName, trackName, connection);

        await connection.CloseAsync();

        return album;
    }

    public TrackInfo CachedTrackToTrackInfo(Track track)
    {
        return new TrackInfo
        {
            AlbumName = track.AlbumName,
            ArtistName = track.ArtistName,
            TrackUrl = track.LastFmUrl,
            TrackName = track.Name,
            Mbid = track.Mbid
        };
    }

    public async Task<List<TrackAutoCompleteSearchModel>> GetLatestTracks(ulong discordUserId, bool cacheEnabled = true)
    {
        try
        {
            var cacheKey = $"user-recent-tracks-{discordUserId}";

            var cacheAvailable = this._cache.TryGetValue(cacheKey, out List<TrackAutoCompleteSearchModel> userArtists);
            if (cacheAvailable && cacheEnabled)
            {
                return userArtists;
            }

            await using var db = await this._contextFactory.CreateDbContextAsync();
            var user = await db.Users.FirstOrDefaultAsync(f => f.DiscordUserId == discordUserId);

            if (user == null)
            {
                return new List<TrackAutoCompleteSearchModel> { new(Constants.AutoCompleteLoginRequired) };
            }

            await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
            await connection.OpenAsync();

            var plays = await PlayRepository.GetUserPlaysWithinTimeRange(user.UserId, connection, DateTime.UtcNow.AddDays(-2));

            var tracks = plays
                .OrderByDescending(o => o.TimePlayed)
                .Select(s => new TrackAutoCompleteSearchModel(s.ArtistName, s.TrackName))
                .Distinct()
                .ToList();

            this._cache.Set(cacheKey, tracks, TimeSpan.FromSeconds(30));

            return tracks;
        }
        catch (Exception e)
        {
            Log.Error($"Error in {nameof(GetLatestTracks)}", e);
            throw;
        }
    }

    public async Task<List<TrackAutoCompleteSearchModel>> GetRecentTopTracks(ulong discordUserId, bool cacheEnabled = true)
    {
        try
        {
            var cacheKey = $"user-recent-top-tracks-{discordUserId}";

            var cacheAvailable = this._cache.TryGetValue(cacheKey, out List<TrackAutoCompleteSearchModel> userAlbums);
            if (cacheAvailable && cacheEnabled)
            {
                return userAlbums;
            }

            await using var db = await this._contextFactory.CreateDbContextAsync();
            var user = await db.Users.FirstOrDefaultAsync(f => f.DiscordUserId == discordUserId);

            if (user == null)
            {
                return new List<TrackAutoCompleteSearchModel> { new(Constants.AutoCompleteLoginRequired) };
            }

            await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
            await connection.OpenAsync();

            var plays = await PlayRepository.GetUserPlaysWithinTimeRange(user.UserId, connection, DateTime.UtcNow.AddDays(-20));

            var tracks = plays
                .GroupBy(g => new TrackAutoCompleteSearchModel(g.ArtistName, g.TrackName))
                .OrderByDescending(o => o.Count())
                .Select(s => s.Key)
                .ToList();

            this._cache.Set(cacheKey, tracks, TimeSpan.FromSeconds(120));

            return tracks;
        }
        catch (Exception e)
        {
            Log.Error($"Error in {nameof(GetRecentTopTracks)}", e);
            throw;
        }
    }

    public async Task<List<TrackAutoCompleteSearchModel>> SearchThroughTracks(string searchValue, bool cacheEnabled = true)
    {
        try
        {
            const string cacheKey = "tracks-all";

            var cacheAvailable = this._cache.TryGetValue(cacheKey, out List<TrackAutoCompleteSearchModel> tracks);
            if (!cacheAvailable && cacheEnabled)
            {
                const string sql = "SELECT * " +
                                   "FROM public.tracks " +
                                   "WHERE popularity is not null AND popularity > 5 ";

                DefaultTypeMap.MatchNamesWithUnderscores = true;
                await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
                await connection.OpenAsync();

                var trackQuery = (await connection.QueryAsync<Track>(sql)).ToList();

                tracks = trackQuery
                    .Select(s => new TrackAutoCompleteSearchModel(s.ArtistName, s.Name, s.Popularity))
                    .ToList();

                this._cache.Set(cacheKey, tracks, TimeSpan.FromHours(2));
            }

            searchValue = searchValue.ToLower();

            var results = tracks.Where(w =>
                    w.Name.ToLower().StartsWith(searchValue) ||
                    w.Artist.ToLower().StartsWith(searchValue) ||
                    w.Name.ToLower().Contains(searchValue) ||
                    w.Artist.ToLower().Contains(searchValue))
                .OrderByDescending(o => o.Popularity)
                .ToList();

            return results;
        }
        catch (Exception e)
        {
            Log.Error($"Error in {nameof(SearchThroughTracks)}", e);
            throw;
        }
    }
}
