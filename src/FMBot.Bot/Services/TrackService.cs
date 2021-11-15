using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Dapper;
using FMBot.Bot.Configurations;
using FMBot.Bot.Models;
using FMBot.Bot.Services.ThirdParty;
using FMBot.Domain.Models;
using FMBot.LastFM.Repositories;
using FMBot.Persistence.Domain.Models;
using Microsoft.AspNetCore.WebUtilities;
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

        public TrackService(IHttpClientFactory httpClientFactory, LastFmRepository lastFmRepository, IOptions<BotSettings> botSettings, SpotifyService spotifyService)
        {
            this._lastFmRepository = lastFmRepository;
            this._spotifyService = spotifyService;
            this._client = httpClientFactory.CreateClient();
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
                    var artistName = description.Split(" - ")[0];
                    var trackName = description.Split(" - ")[1].Replace("\\", "");
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
                Log.Error("Getting while getting track for description: {description}", description, e);
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

        public async Task<List<UserTrack>> GetAlbumTracksPlaycounts(List<AlbumTrack> albumTracks, int userId, string artistName)
        {
            const string sql = "SELECT user_track_id, user_id, name, artist_name, playcount" +
               " FROM public.user_tracks where user_id = @userId AND UPPER(artist_name) = UPPER(CAST(@artistName AS CITEXT));";

            DefaultTypeMap.MatchNamesWithUnderscores = true;
            await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
            await connection.OpenAsync();

            var userTracks = (await connection.QueryAsync<UserTrack>(sql, new
            {
                userId,
                artistName
            })).ToList();

            return userTracks
                .Where(w => albumTracks.Select(s => s.TrackName.Replace("(", "").Replace("-", "").Replace(")", "").TrimEnd().TrimStart().ToLower())
                    .Contains(w.Name.Replace("(", "").Replace("-", "").Replace(")", "").TrimEnd().TrimStart().ToLower()))
                .ToList();
        }

    }
}
