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

public class SpotifyService
{
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

    public async Task<FullArtist> GetArtistFromSpotify(string artistName)
    {
        var spotify = GetSpotifyWebApi();

        var searchRequest = new SearchRequest(SearchRequest.Types.Artist, artistName)
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
                return spotifyArtist;
            }
        }

        return null;
    }

    public async Task<FullTrack> GetTrackFromSpotify(string trackName, string artistName)
    {
        //Create the auth object
        var spotify = GetSpotifyWebApi();

        var searchRequest = new SearchRequest(SearchRequest.Types.Track, $"track:{trackName} artist:{artistName}");

        var results = await spotify.Search.Item(searchRequest);
        Statistics.SpotifyApiCalls.Inc();

        if (results.Tracks.Items?.Any() == true)
        {
            var spotifyTrack = results.Tracks.Items
                .OrderByDescending(o => o.Popularity)
                .FirstOrDefault(w => w.Name.ToLower() == trackName.ToLower() && w.Artists.Select(s => s.Name.ToLower()).Contains(artistName.ToLower()));

            if (spotifyTrack != null)
            {
                return spotifyTrack;
            }
        }

        return null;
    }

    public async Task<FullAlbum> GetAlbumFromSpotify(string albumName, string artistName)
    {
        //Create the auth object
        var spotify = GetSpotifyWebApi();

        var searchQuery = $"{albumName} {artistName}";
        if (searchQuery.Length > 100)
        {
            searchQuery = searchQuery[..100];
        }

        var searchRequest = new SearchRequest(SearchRequest.Types.Album, searchQuery);

        var results = await spotify.Search.Item(searchRequest);
        Statistics.SpotifyApiCalls.Inc();

        if (results.Albums.Items?.Any() == true)
        {
            var spotifyAlbum = results.Albums.Items
                .FirstOrDefault(w => w.Name.ToLower() == albumName.ToLower() && w.Artists.Select(s => s.Name.ToLower()).Contains(artistName.ToLower()));

            if (spotifyAlbum != null)
            {
                return await GetAlbumById(spotifyAlbum.Id);
            }
        }

        return null;
    }

    public async Task<FullTrack> GetTrackById(string spotifyId)
    {
        //Create the auth object
        var spotify = GetSpotifyWebApi();

        Statistics.SpotifyApiCalls.Inc();
        return await spotify.Tracks.Get(spotifyId);
    }

    public async Task<FullAlbum> GetAlbumById(string spotifyId)
    {
        //Create the auth object
        var spotify = GetSpotifyWebApi();

        Statistics.SpotifyApiCalls.Inc();
        return await spotify.Albums.Get(spotifyId);
    }

    public async Task<TrackAudioFeatures> GetAudioFeaturesFromSpotify(string spotifyId)
    {
        //Create the auth object
        var spotify = GetSpotifyWebApi();

        var result = await spotify.Tracks.GetAudioFeatures(spotifyId);
        Statistics.SpotifyApiCalls.Inc();

        return result;
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

    public void InitApiClientConfig()
    {
        if (PublicProperties.SpotifyConfig == null)
        {
            PublicProperties.SpotifyConfig = SpotifyClientConfig
                .CreateDefault()
                .WithHTTPClient(new NetHttpClient(this._httpClient))
                .WithAuthenticator(new ClientCredentialsAuthenticator(this._botSettings.Spotify.Key,
                    this._botSettings.Spotify.Secret));
        }
    }
}
