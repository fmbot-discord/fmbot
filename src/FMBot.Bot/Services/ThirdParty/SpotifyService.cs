using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using FMBot.Domain;
using FMBot.Domain.Models;
using FMBot.Persistence.Domain.Models;
using FMBot.Persistence.EntityFrameWork;
using FMBot.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using Serilog;
using SpotifyAPI.Web;
using SpotifyAPI.Web.Http;

namespace FMBot.Bot.Services.ThirdParty;

public sealed record SpotifyLookup<T>(T Item, bool Failed) where T : class
{
    public static SpotifyLookup<T> Found(T item) => new(item, false);
    public static readonly SpotifyLookup<T> NotFound = new(null, false);
    public static readonly SpotifyLookup<T> Unavailable = new(null, true);
}

public class SpotifyService
{
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(5);

    private readonly IDbContextFactory<FMBotDbContext> _contextFactory;
    private readonly BotSettings _botSettings;
    private readonly HttpClient _httpClient;

    public SpotifyService(IDbContextFactory<FMBotDbContext> contextFactory,
        IOptions<BotSettings> botSettings,
        HttpClient httpClient)
    {
        this._contextFactory = contextFactory;
        this._httpClient = httpClient;
        this._botSettings = botSettings.Value;
    }

    public async Task<SearchResponse> GetSearchResultAsync(string searchValue, SearchRequest.Types searchType = SearchRequest.Types.Track)
    {
        var spotify = GetSpotifyWebApi();

        searchValue = searchValue.Replace("- Single", "");
        searchValue = searchValue.Replace("- EP", "");

        var searchRequest = new SearchRequest(searchType, searchValue)
        {
            Limit = 50
        };

        Statistics.SpotifyApiCalls.Inc();
        return await spotify.Search.Item(searchRequest);
    }

    public async Task<SpotifyLookup<FullArtist>> GetArtistFromSpotify(string artistName)
    {
        try
        {
            var spotify = GetSpotifyWebApi();

            var truncatedArtistName = artistName.Length > 100 ? artistName[..100] : artistName;

            var searchRequest = new SearchRequest(SearchRequest.Types.Artist, truncatedArtistName)
            {
                Limit = 50
            };

            var results = await spotify.Search.Item(searchRequest);
            Statistics.SpotifyApiCalls.Inc();

            if (results.Artists.Items?.Any() == true)
            {
                var spotifyArtist = results.Artists.Items
                    .OrderByDescending(o => o.Popularity)
                    .ThenByDescending(o => o.Followers.Total)
                    .FirstOrDefault(w => w.Name.ToLower() == artistName.ToLower());

                if (spotifyArtist != null)
                {
                    return SpotifyLookup<FullArtist>.Found(spotifyArtist);
                }
            }

            return SpotifyLookup<FullArtist>.NotFound;
        }
        catch (APIException e)
        {
            Log.Warning(e, "SpotifyService: Artist search failed for {artistName}", artistName);
            return SpotifyLookup<FullArtist>.Unavailable;
        }
    }

    public async Task<SpotifyLookup<FullTrack>> GetTrackFromSpotify(string trackName, string artistName)
    {
        try
        {
            var spotify = GetSpotifyWebApi();

            var truncatedTrackName = trackName.Length > 100 ? trackName[..100] : trackName;
            var truncatedArtistName = artistName.Length > 100 ? artistName[..100] : artistName;

            var searchRequest = new SearchRequest(SearchRequest.Types.Track, $"track:{truncatedTrackName} artist:{truncatedArtistName}");

            var results = await spotify.Search.Item(searchRequest);
            Statistics.SpotifyApiCalls.Inc();

            if (results.Tracks.Items?.Any() == true)
            {
                var spotifyTrack = results.Tracks.Items
                    .OrderByDescending(o => o.Popularity)
                    .FirstOrDefault(w => w.Name.ToLower() == trackName.ToLower() && w.Artists.Select(s => s.Name.ToLower()).Contains(artistName.ToLower()));

                if (spotifyTrack != null)
                {
                    return SpotifyLookup<FullTrack>.Found(spotifyTrack);
                }
            }

            return SpotifyLookup<FullTrack>.NotFound;
        }
        catch (APIException e)
        {
            Log.Warning(e, "SpotifyService: Track search failed for {artistName} - {trackName}", artistName, trackName);
            return SpotifyLookup<FullTrack>.Unavailable;
        }
    }

    public async Task<SpotifyLookup<FullAlbum>> GetAlbumFromSpotify(string albumName, string artistName)
    {
        try
        {
            var spotify = GetSpotifyWebApi();

            var truncatedAlbumName = albumName.Length > 100 ? albumName[..100] : albumName;
            var truncatedArtistName = artistName.Length > 100 ? artistName[..100] : artistName;

            var searchRequest = new SearchRequest(SearchRequest.Types.Album, $"{truncatedAlbumName} {truncatedArtistName}");

            var results = await spotify.Search.Item(searchRequest);
            Statistics.SpotifyApiCalls.Inc();

            if (results.Albums.Items?.Any() == true)
            {
                var spotifyAlbum = results.Albums.Items
                    .FirstOrDefault(w => w.Name.ToLower() == albumName.ToLower() && w.Artists.Select(s => s.Name.ToLower()).Contains(artistName.ToLower()));

                if (spotifyAlbum != null)
                {
                    return SpotifyLookup<FullAlbum>.Found(await GetAlbumById(spotifyAlbum.Id));
                }
            }

            return SpotifyLookup<FullAlbum>.NotFound;
        }
        catch (APIException e)
        {
            Log.Warning(e, "SpotifyService: Album search failed for {artistName} - {albumName}", artistName, albumName);
            return SpotifyLookup<FullAlbum>.Unavailable;
        }
    }

    public async Task<FullTrack> GetTrackById(string spotifyId)
    {
        var spotify = GetSpotifyWebApi();

        Statistics.SpotifyApiCalls.Inc();
        return await spotify.Tracks.Get(spotifyId);
    }

    public async Task<FullAlbum> GetAlbumById(string spotifyId)
    {
        var spotify = GetSpotifyWebApi();

        Statistics.SpotifyApiCalls.Inc();
        return await spotify.Albums.Get(spotifyId);
    }

    public async Task<FullArtist> GetArtistById(string spotifyId)
    {
        var spotify = GetSpotifyWebApi();

        Statistics.SpotifyApiCalls.Inc();
        return await spotify.Artists.Get(spotifyId);
    }

    public async Task<List<FullTrack>> GetArtistTopTracks(string spotifyId, string market = "US")
    {
        var spotify = GetSpotifyWebApi();

        try
        {
            Statistics.SpotifyApiCalls.Inc();
            var result = await spotify.Artists.GetTopTracks(spotifyId, new ArtistsTopTracksRequest(market));
            return result?.Tracks ?? [];
        }
        catch (APIException e)
        {
            Log.Warning(e, "SpotifyService: Top tracks lookup failed for {spotifyId}", spotifyId);
            return [];
        }
    }

    public async Task<TrackAudioFeatures> GetAudioFeaturesFromSpotify(string spotifyId)
    {
        try
        {
            var spotify = GetSpotifyWebApi();

            var result = await spotify.Tracks.GetAudioFeatures(spotifyId);
            Statistics.SpotifyApiCalls.Inc();

            return result;
        }
        catch (Exception e)
        {
            Log.Warning(e, "SpotifyService: Failed to get audio features for {spotifyId}", spotifyId);
            return null;
        }
    }

    public async Task<ICollection<Track>> GetDatabaseAlbumTracks(int albumId)
    {
        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        var albumTracks = await TrackRepository.GetAlbumTracks(albumId, connection);
        await connection.CloseAsync();

        return albumTracks;
    }

    private SpotifyClient GetSpotifyWebApi()
    {
        InitApiClientConfig();

        return new SpotifyClient(PublicProperties.SpotifyConfig);
    }

    private void InitApiClientConfig()
    {
        if (PublicProperties.SpotifyConfig == null)
        {
            PublicProperties.SpotifyConfig = SpotifyClientConfig
                .CreateDefault()
                .WithHTTPClient(new NetHttpClient(this._httpClient))
                .WithRetryHandler(new SimpleRetryHandler((delay, cancellationToken) => delay > MaxRetryDelay
                    ? throw new APIException($"Spotify requested a retry after {delay.TotalSeconds:F0}s, giving up")
                    : Task.Delay(delay, cancellationToken))
                {
                    RetryTimes = 2,
                    RetryAfter = TimeSpan.FromMilliseconds(500),
                    TooManyRequestsConsumesARetry = true
                })
                .WithAuthenticator(new ClientCredentialsAuthenticator(this._botSettings.Spotify.Key,
                    this._botSettings.Spotify.Secret));
        }
    }
}
