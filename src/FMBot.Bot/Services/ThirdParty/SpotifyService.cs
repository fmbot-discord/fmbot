using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Discord;
using FMBot.Domain;
using FMBot.Domain.Models;
using FMBot.Persistence.Domain.Models;
using FMBot.Persistence.EntityFrameWork;
using FMBot.Persistence.Repositories;
using Genius.Models.Song;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Npgsql;
using Serilog;
using SpotifyAPI.Web;
using SpotifyAPI.Web.Http;
using Album = FMBot.Persistence.Domain.Models.Album;
using Artist = FMBot.Persistence.Domain.Models.Artist;

namespace FMBot.Bot.Services.ThirdParty;

public class SpotifyService
{
    private readonly IDbContextFactory<FMBotDbContext> _contextFactory;
    private readonly BotSettings _botSettings;
    private readonly ArtistRepository _artistRepository;
    private readonly AlbumRepository _albumRepository;
    private readonly TrackRepository _trackRepository;
    private readonly IMemoryCache _cache;
    private readonly MusicBrainzService _musicBrainzService;
    private readonly HttpClient _httpClient;

    public SpotifyService(IDbContextFactory<FMBotDbContext> contextFactory,
        IOptions<BotSettings> botSettings,
        ArtistRepository artistRepository,
        TrackRepository trackRepository,
        AlbumRepository albumRepository,
        IMemoryCache cache,
        MusicBrainzService musicBrainzService,
        HttpClient httpClient)
    {
        this._contextFactory = contextFactory;
        this._artistRepository = artistRepository;
        this._trackRepository = trackRepository;
        this._albumRepository = albumRepository;
        this._cache = cache;
        this._musicBrainzService = musicBrainzService;
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

    public async Task<Artist> GetOrStoreArtistAsync(ArtistInfo artistInfo, string artistNameBeforeCorrect = null)
    {
        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        try
        {
            var dbArtist = await this._artistRepository.GetArtistForName(artistInfo.ArtistName, connection, true);

            if (dbArtist == null)
            {
                await using var db = await this._contextFactory.CreateDbContextAsync();
                var spotifyArtist = await GetArtistFromSpotify(artistInfo.ArtistName);

                var artistToAdd = new Artist
                {
                    Name = artistInfo.ArtistName,
                    LastFmUrl = artistInfo.ArtistUrl,
                    Mbid = artistInfo.Mbid,
                    LastFmDescription = artistInfo.Description,
                    LastfmDate = DateTime.UtcNow
                };

                var musicBrainzUpdated = await this._musicBrainzService.AddMusicBrainzDataToArtistAsync(artistToAdd);

                if (musicBrainzUpdated.Updated)
                {
                    artistToAdd = musicBrainzUpdated.Artist;
                }

                if (spotifyArtist != null)
                {
                    artistToAdd.SpotifyId = spotifyArtist.Id;
                    artistToAdd.Popularity = spotifyArtist.Popularity;

                    if (spotifyArtist.Images.Any())
                    {
                        artistToAdd.SpotifyImageUrl = spotifyArtist.Images.OrderByDescending(o => o.Height).First().Url;
                        artistToAdd.SpotifyImageDate = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc);

                        if (artistInfo.ArtistUrl != null)
                        {
                            this._cache.Set(ArtistsService.CacheKeyForArtist(artistInfo.ArtistUrl), artistToAdd.SpotifyImageUrl, TimeSpan.FromMinutes(5));
                        }
                    }

                    await db.Artists.AddAsync(artistToAdd);
                    await db.SaveChangesAsync();

                    if (artistToAdd.Id == 0)
                    {
                        throw new Exception("Artist id is 0!");
                    }
                    if (spotifyArtist.Genres.Any())
                    {
                        await this._artistRepository
                            .AddOrUpdateArtistGenres(artistToAdd.Id, spotifyArtist.Genres.Select(s => s), connection);
                    }
                }
                else
                {
                    await db.Artists.AddAsync(artistToAdd);
                    await db.SaveChangesAsync();

                    if (artistToAdd.Id == 0)
                    {
                        throw new Exception("Artist id is 0!");
                    }
                }

                if (spotifyArtist != null && spotifyArtist.Genres.Any())
                {
                    artistToAdd.ArtistGenres = spotifyArtist.Genres.Select(s => new ArtistGenre
                    {
                        Name = s
                    }).ToList();
                }

                if (artistNameBeforeCorrect != null && !string.Equals(artistNameBeforeCorrect, artistInfo.ArtistName, StringComparison.CurrentCultureIgnoreCase))
                {
                    await this._artistRepository
                        .AddOrUpdateArtistAlias(artistToAdd.Id, artistNameBeforeCorrect, connection);
                }

                return artistToAdd;
            }

            if (artistNameBeforeCorrect != null && !string.Equals(artistNameBeforeCorrect, artistInfo.ArtistName, StringComparison.CurrentCultureIgnoreCase))
            {
                await this._artistRepository
                    .AddOrUpdateArtistAlias(dbArtist.Id, artistNameBeforeCorrect, connection);
            }

            if (artistInfo.Description != null && dbArtist.LastFmDescription != artistInfo.Description)
            {
                await using var db = await this._contextFactory.CreateDbContextAsync();

                dbArtist.LastFmDescription = artistInfo.Description;
                dbArtist.LastfmDate = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc);
                db.Entry(dbArtist).State = EntityState.Modified;

                await db.SaveChangesAsync();
            }

            var musicBrainzUpdate = await this._musicBrainzService.AddMusicBrainzDataToArtistAsync(dbArtist);

            if (musicBrainzUpdate.Updated)
            {
                dbArtist = musicBrainzUpdate.Artist;

                await using var db = await this._contextFactory.CreateDbContextAsync();
                db.Entry(dbArtist).State = EntityState.Modified;
                await db.SaveChangesAsync();
            }

            if (dbArtist.SpotifyImageUrl == null || dbArtist.SpotifyImageDate < DateTime.UtcNow.AddDays(-15))
            {
                await using var db = await this._contextFactory.CreateDbContextAsync();

                var spotifyArtist = await GetArtistFromSpotify(artistInfo.ArtistName);

                if (spotifyArtist != null && spotifyArtist.Images.Any())
                {
                    dbArtist.SpotifyImageUrl = spotifyArtist.Images.OrderByDescending(o => o.Height).First().Url;

                    dbArtist.SpotifyId = spotifyArtist.Id;
                    dbArtist.Popularity = spotifyArtist.Popularity;

                    if (artistInfo.ArtistUrl != null)
                    {
                        this._cache.Set(ArtistsService.CacheKeyForArtist(artistInfo.ArtistUrl), dbArtist.SpotifyImageUrl, TimeSpan.FromMinutes(5));
                    }
                }

                if (spotifyArtist != null && spotifyArtist.Genres.Any())
                {
                    await this._artistRepository
                        .AddOrUpdateArtistGenres(dbArtist.Id, spotifyArtist.Genres.Select(s => s), connection);
                }

                dbArtist.SpotifyImageDate = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc);
                db.Entry(dbArtist).State = EntityState.Modified;
                await db.SaveChangesAsync();

                if (spotifyArtist != null && spotifyArtist.Genres.Any())
                {
                    dbArtist.ArtistGenres = spotifyArtist.Genres.Select(s => new ArtistGenre
                    {
                        Name = s
                    }).ToList();
                }
            }

            await connection.CloseAsync();
            return dbArtist;

        }
        catch (Exception e)
        {
            Log.Error(e, "Something went wrong while retrieving artist image");
            return new Artist
            {
                Name = artistInfo.ArtistName,
                LastFmUrl = artistInfo.ArtistUrl
            };
        }
    }

    private async Task<FullArtist> GetArtistFromSpotify(string artistName)
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

    public async Task<Track> GetOrStoreTrackAsync(TrackInfo trackInfo)
    {
        try
        {
            await using var db = await this._contextFactory.CreateDbContextAsync();

            await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
            await connection.OpenAsync();

            var dbTrack = await TrackRepository.GetTrackForName(trackInfo.ArtistName, trackInfo.TrackName, connection);

            if (dbTrack == null)
            {
                var trackToAdd = new Track
                {
                    Name = trackInfo.TrackName,
                    AlbumName = trackInfo.AlbumName,
                    ArtistName = trackInfo.ArtistName,
                    DurationMs = (int)trackInfo.Duration,
                    LastFmUrl = trackInfo.TrackUrl,
                    LastFmDescription = trackInfo.Description,
                    LastfmDate = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc)
                };

                var artist = await this._artistRepository.GetArtistForName(trackInfo.ArtistName, connection);

                if (artist != null)
                {
                    trackToAdd.ArtistId = artist.Id;
                }

                var spotifyTrack = await GetTrackFromSpotify(trackInfo.TrackName, trackInfo.ArtistName.ToLower());

                if (spotifyTrack != null)
                {
                    trackToAdd.SpotifyId = spotifyTrack.Id;
                    trackToAdd.DurationMs = spotifyTrack.DurationMs;
                    trackToAdd.Popularity = spotifyTrack.Popularity;

                    var audioFeatures = await GetAudioFeaturesFromSpotify(spotifyTrack.Id);

                    if (audioFeatures != null)
                    {
                        trackToAdd.Key = audioFeatures.Key;
                        trackToAdd.Tempo = audioFeatures.Tempo;
                        trackToAdd.Acousticness = audioFeatures.Acousticness;
                        trackToAdd.Danceability = audioFeatures.Danceability;
                        trackToAdd.Energy = audioFeatures.Energy;
                        trackToAdd.Instrumentalness = audioFeatures.Instrumentalness;
                        trackToAdd.Liveness = audioFeatures.Liveness;
                        trackToAdd.Loudness = audioFeatures.Loudness;
                        trackToAdd.Speechiness = audioFeatures.Speechiness;
                        trackToAdd.Valence = audioFeatures.Valence;
                    }
                }

                trackToAdd.SpotifyLastUpdated = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc);

                await db.Tracks.AddAsync(trackToAdd);
                await db.SaveChangesAsync();

                return trackToAdd;
            }
            if (!dbTrack.ArtistId.HasValue)
            {
                var artist = await this._artistRepository.GetArtistForName(trackInfo.ArtistName, connection);

                if (artist != null)
                {
                    dbTrack.ArtistId = artist.Id;
                    db.Entry(dbTrack).State = EntityState.Modified;
                }
            }

            if (dbTrack.LastFmUrl == null && trackInfo.TrackUrl != null)
            {
                dbTrack.LastFmUrl = trackInfo.TrackUrl;
                dbTrack.LastfmDate = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc);
                db.Entry(dbTrack).State = EntityState.Modified;
            }

            if (trackInfo.Description != null && dbTrack.LastFmDescription != trackInfo.Description)
            {
                dbTrack.LastFmDescription = trackInfo.Description;
                dbTrack.LastfmDate = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc);
                db.Entry(dbTrack).State = EntityState.Modified;
            }

            if (dbTrack.DurationMs == null && trackInfo.Duration.HasValue)
            {
                dbTrack.DurationMs = (int)trackInfo.Duration.Value;
                dbTrack.LastfmDate = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc);
                db.Entry(dbTrack).State = EntityState.Modified;
            }

            var monthsToGoBack = !string.IsNullOrEmpty(dbTrack.SpotifyId) && !dbTrack.Energy.HasValue ? 1 : 3;
            if (dbTrack.SpotifyLastUpdated < DateTime.UtcNow.AddMonths(-monthsToGoBack))
            {
                var spotifyTrack = await GetTrackFromSpotify(trackInfo.TrackName, trackInfo.ArtistName.ToLower());

                if (spotifyTrack != null)
                {
                    dbTrack.SpotifyId = spotifyTrack.Id;
                    dbTrack.DurationMs = spotifyTrack.DurationMs;
                    dbTrack.Popularity = spotifyTrack.Popularity;

                    var audioFeatures = await GetAudioFeaturesFromSpotify(spotifyTrack.Id);

                    if (audioFeatures != null)
                    {
                        dbTrack.Key = audioFeatures.Key;
                        dbTrack.Tempo = audioFeatures.Tempo;
                        dbTrack.Acousticness = audioFeatures.Acousticness;
                        dbTrack.Danceability = audioFeatures.Danceability;
                        dbTrack.Energy = audioFeatures.Energy;
                        dbTrack.Instrumentalness = audioFeatures.Instrumentalness;
                        dbTrack.Liveness = audioFeatures.Liveness;
                        dbTrack.Loudness = audioFeatures.Loudness;
                        dbTrack.Speechiness = audioFeatures.Speechiness;
                        dbTrack.Valence = audioFeatures.Valence;
                    }
                }

                dbTrack.SpotifyLastUpdated = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc);
                db.Entry(dbTrack).State = EntityState.Modified;
            }

            await db.SaveChangesAsync();

            await connection.CloseAsync();

            return dbTrack;
        }
        catch (Exception e)
        {
            Log.Error(e, "Something went wrong while retrieving track info");
            return null;
        }
    }

    private async Task<FullTrack> GetTrackFromSpotify(string trackName, string artistName)
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

    private async Task<FullAlbum> GetAlbumFromSpotify(string albumName, string artistName)
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

    private async Task<TrackAudioFeatures> GetAudioFeaturesFromSpotify(string spotifyId)
    {
        //Create the auth object
        var spotify = GetSpotifyWebApi();

        var result = await spotify.Tracks.GetAudioFeatures(spotifyId);
        Statistics.SpotifyApiCalls.Inc();

        return result;
    }


    public async Task<Album> GetOrStoreSpotifyAlbumAsync(AlbumInfo albumInfo, bool fastSpotifyCacheExpiry = false)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();

        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        var dbAlbum = await this._albumRepository.GetAlbumForName(albumInfo.ArtistName, albumInfo.AlbumName, connection);

        if (dbAlbum == null)
        {
            var albumToAdd = new Album
            {
                Name = albumInfo.AlbumName,
                ArtistName = albumInfo.ArtistName,
                LastFmUrl = albumInfo.AlbumUrl,
                Mbid = albumInfo.Mbid,
                LastfmImageUrl = albumInfo.AlbumCoverUrl,
                LastFmDescription = albumInfo.Description,
                LastfmDate = DateTime.UtcNow
            };

            var artist = await this._artistRepository.GetArtistForName(albumInfo.ArtistName, connection);

            if (artist != null && artist.Id != 0)
            {
                albumToAdd.ArtistId = artist.Id;
            }

            var spotifyAlbum = await GetAlbumFromSpotify(albumInfo.AlbumName, albumInfo.ArtistName.ToLower());

            if (spotifyAlbum != null)
            {
                albumToAdd.SpotifyId = spotifyAlbum.Id;
                albumToAdd.Label = spotifyAlbum.Label;
                albumToAdd.Popularity = spotifyAlbum.Popularity;
                albumToAdd.SpotifyImageUrl = spotifyAlbum.Images.OrderByDescending(o => o.Height).First().Url;
                albumToAdd.ReleaseDate = spotifyAlbum.ReleaseDate;
                albumToAdd.ReleaseDatePrecision = spotifyAlbum.ReleaseDatePrecision;
            }

            var coverUrl = albumInfo.AlbumCoverUrl ?? albumToAdd.SpotifyImageUrl;
            if (coverUrl != null && albumInfo.AlbumUrl != null)
            {
                this._cache.Set(AlbumService.CacheKeyForAlbumCover(albumInfo.AlbumUrl), coverUrl, TimeSpan.FromMinutes(5));
            }

            albumToAdd.SpotifyImageDate = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc);

            await db.Albums.AddAsync(albumToAdd);
            await db.SaveChangesAsync();

            if (spotifyAlbum != null)
            {
                await GetOrStoreAlbumTracks(spotifyAlbum.Tracks.Items, albumInfo, albumToAdd.Id, connection);
            }

            await connection.CloseAsync();

            return albumToAdd;
        }
        if (albumInfo.AlbumCoverUrl != null)
        {
            dbAlbum.LastfmImageUrl = albumInfo.AlbumCoverUrl;
            db.Entry(dbAlbum).State = EntityState.Modified;
            await db.SaveChangesAsync();
        }
        if (albumInfo.Description != null && dbAlbum.LastFmDescription != albumInfo.Description)
        {
            dbAlbum.LastFmDescription = albumInfo.Description;
            dbAlbum.LastfmDate = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc);
            db.Entry(dbAlbum).State = EntityState.Modified;
        }

        if (dbAlbum.Artist == null)
        {
            var artist = await this._artistRepository.GetArtistForName(albumInfo.ArtistName, connection);

            if (artist != null && artist.Id != 0)
            {
                dbAlbum.ArtistId = artist.Id;
                db.Entry(dbAlbum).State = EntityState.Modified;
                await db.SaveChangesAsync();
            }
        }

        var monthsToGoBack = !string.IsNullOrEmpty(dbAlbum.SpotifyId) && dbAlbum.ReleaseDate == null ? 1 : 3;
        if (dbAlbum.SpotifyImageDate < DateTime.UtcNow.AddMonths(-monthsToGoBack) || dbAlbum.SpotifyId == null && fastSpotifyCacheExpiry)
        {
            var spotifyAlbum = await GetAlbumFromSpotify(albumInfo.AlbumName, albumInfo.ArtistName.ToLower());

            if (spotifyAlbum != null)
            {
                dbAlbum.SpotifyId = spotifyAlbum.Id;
                dbAlbum.Label = spotifyAlbum.Label;
                dbAlbum.Popularity = spotifyAlbum.Popularity;
                dbAlbum.SpotifyImageUrl = spotifyAlbum.Images.OrderByDescending(o => o.Height).First().Url;
                dbAlbum.ReleaseDate = spotifyAlbum.ReleaseDate;
                dbAlbum.ReleaseDatePrecision = spotifyAlbum.ReleaseDatePrecision;
            }

            var coverUrl = albumInfo.AlbumCoverUrl ?? dbAlbum.SpotifyImageUrl;
            if (coverUrl != null && albumInfo.AlbumUrl != null)
            {
                this._cache.Set(AlbumService.CacheKeyForAlbumCover(albumInfo.AlbumUrl), coverUrl, TimeSpan.FromMinutes(5));
            }

            dbAlbum.SpotifyImageDate = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc);
            dbAlbum.LastfmImageUrl = albumInfo.AlbumCoverUrl;

            db.Entry(dbAlbum).State = EntityState.Modified;

            await db.SaveChangesAsync();

            if (spotifyAlbum != null)
            {
                await GetOrStoreAlbumTracks(spotifyAlbum.Tracks.Items, albumInfo, dbAlbum.Id, connection);
            }
        }

        await connection.CloseAsync();

        return dbAlbum;
    }

    private async Task GetOrStoreAlbumTracks(IEnumerable<SimpleTrack> simpleTracks, AlbumInfo albumInfo,
        int albumId, NpgsqlConnection connection)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        var dbTracks = new List<Track>();
        foreach (var track in simpleTracks.OrderBy(o => o.TrackNumber))
        {
            var dbTrack = await TrackRepository.GetTrackForName(albumInfo.ArtistName, track.Name, connection);

            if (dbTrack != null)
            {
                dbTracks.Add(dbTrack);
            }
            else
            {
                var trackToAdd = new Track
                {
                    Name = track.Name,
                    AlbumName = albumInfo.AlbumName,
                    DurationMs = track.DurationMs,
                    SpotifyId = track.Id,
                    ArtistName = albumInfo.ArtistName,
                    SpotifyLastUpdated = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc),
                    AlbumId = albumId
                };

                await db.Tracks.AddAsync(trackToAdd);

                dbTracks.Add(trackToAdd);
            }
        }

        await db.SaveChangesAsync();
    }

    public async Task<ICollection<Track>> GetExistingAlbumTracks(int albumId)
    {
        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        var albumTracks = await this._trackRepository.GetAlbumTracks(albumId, connection);
        await connection.CloseAsync();

        return albumTracks;
    }

    private SpotifyClient GetSpotifyWebApi()
    {
        var config = SpotifyClientConfig
            .CreateDefault()
            .WithHTTPClient(new NetHttpClient(this._httpClient))
            .WithAuthenticator(new ClientCredentialsAuthenticator(this._botSettings.Spotify.Key, this._botSettings.Spotify.Secret));

        return new SpotifyClient(config);
    }

    public static RecentTrack SpotifyGameToRecentTrack(SpotifyGame spotifyActivity)
    {
        return new RecentTrack
        {
            TrackName = spotifyActivity.TrackTitle,
            AlbumName = spotifyActivity.AlbumTitle,
            ArtistName = spotifyActivity.Artists.First(),
            AlbumCoverUrl = spotifyActivity.AlbumArtUrl,
            TrackUrl = spotifyActivity.TrackUrl
        };
    }
}
