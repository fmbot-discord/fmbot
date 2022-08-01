using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Dapper;
using FMBot.Bot.Extensions;
using FMBot.Bot.Models;
using FMBot.Bot.Services.ThirdParty;
using FMBot.Domain.Models;
using FMBot.LastFM.Repositories;
using FMBot.Persistence.Domain.Models;
using FMBot.Persistence.Repositories;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Npgsql;
using Serilog;

namespace FMBot.Bot.Services
{
    public class TrackService
    {
        private readonly LastFmRepository _lastFmRepository;
        private readonly SpotifyService _spotifyService;
        private readonly HttpClient _client;
        private readonly BotSettings _botSettings;
        private readonly IMemoryCache _cache;
        private readonly TrackRepository _trackRepository;

        public TrackService(HttpClient httpClient, LastFmRepository lastFmRepository, IOptions<BotSettings> botSettings, SpotifyService spotifyService, IMemoryCache memoryCache, TrackRepository trackRepository)
        {
            this._lastFmRepository = lastFmRepository;
            this._spotifyService = spotifyService;
            this._cache = memoryCache;
            this._trackRepository = trackRepository;
            this._client = httpClient;
            this._botSettings = botSettings.Value;
        }

        public async Task<TrackSearchResult> GetTrackFromLink(string description, bool possiblyContainsLinks = true)
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

                if (!description.Contains("-"))
                {
                    var lastFmResult = await this._lastFmRepository.SearchTrackAsync(description);
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
                    if (splitDesc.Length == 3)
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

                    var trackLfm = await this._lastFmRepository.GetTrackInfoAsync(trackName, artistName);

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
            trackAudioFeatureDescription.Append($"**Valence (musical positiveness)**: **__{avgCurrentValence:P}__**");
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

        public async Task<Track> GetTrackFromDatabase(string artistName, string trackName)
        {
            if (string.IsNullOrWhiteSpace(artistName) || string.IsNullOrWhiteSpace(trackName))
            {
                return null;
            }

            await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
            await connection.OpenAsync();

            var album = await this._trackRepository.GetTrackForName(artistName, trackName, connection);

            await connection.CloseAsync();

            return album;
        }

        public TrackInfo CachedTrackToTrackInfo(Track track)
        {
            return new TrackInfo
            {
                AlbumName = track.AlbumName,
                ArtistName = track.ArtistName,
                TrackName = track.Name,
                Mbid = track.Mbid
            };
        }
    }
}
