using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using FMBot.Bot.Configurations;
using FMBot.Domain.Models;
using FMBot.LastFM.Domain.Models;
using FMBot.Persistence.Domain.Models;
using FMBot.Persistence.EntityFrameWork;
using Microsoft.EntityFrameworkCore;
using Serilog;
using SpotifyAPI.Web;
using Artist = FMBot.Persistence.Domain.Models.Artist;

namespace FMBot.Bot.Services.ThirdParty
{
    public class SpotifyService
    {
        private readonly IDbContextFactory<FMBotDbContext> _contextFactory;

        public SpotifyService(IDbContextFactory<FMBotDbContext> contextFactory)
        {
            this._contextFactory = contextFactory;
        }

        public async Task<SearchResponse> GetSearchResultAsync(string searchValue, SearchRequest.Types searchType = SearchRequest.Types.Track)
        {
            var spotify = GetSpotifyWebApi();

            searchValue = searchValue.Replace("- Single", "");
            searchValue = searchValue.Replace("- EP", "");

            var searchRequest = new SearchRequest(searchType, searchValue);

            return await spotify.Search.Item(searchRequest);
        }

        public async Task<string> GetOrStoreArtistImageAsync(ArtistInfoLfmResponse lastFmArtistInfoLfm, string artistNameBeforeCorrect)
        {
            await using var db = this._contextFactory.CreateDbContext();
            var dbArtist = await db.Artists
                .Include(i => i.ArtistAliases)
                .Include(i => i.ArtistGenres)
                .AsQueryable()
                .FirstOrDefaultAsync(f => f.Name.ToLower() == lastFmArtistInfoLfm.Artist.Name.ToLower());

            var imageUrlToReturn = "";
            try
            {
                if (dbArtist == null)
                {
                    var spotifyArtist = await GetArtistFromSpotify(lastFmArtistInfoLfm.Artist.Name);

                    var artistToAdd = new Artist
                    {
                        Name = lastFmArtistInfoLfm.Artist.Name,
                        LastFmUrl = lastFmArtistInfoLfm.Artist.Url,
                        Mbid = !string.IsNullOrEmpty(lastFmArtistInfoLfm.Artist.Mbid) ? Guid.Parse(lastFmArtistInfoLfm.Artist.Mbid) : null
                    };

                    if (spotifyArtist != null)
                    {
                        artistToAdd.SpotifyId = spotifyArtist.Id;
                        artistToAdd.Popularity = spotifyArtist.Popularity;

                        if (spotifyArtist.Images.Any())
                        {
                            artistToAdd.SpotifyImageUrl = spotifyArtist.Images.OrderByDescending(o => o.Height).First().Url;
                            artistToAdd.SpotifyImageDate = DateTime.UtcNow;
                            imageUrlToReturn = artistToAdd.SpotifyImageUrl;
                        }

                        if (spotifyArtist.Genres.Any())
                        {
                            var genresToAdd = spotifyArtist.Genres.Select(s => new ArtistGenre
                            {
                                Artist = artistToAdd,
                                Name = s
                            });

                            await db.ArtistGenres.AddRangeAsync(genresToAdd);
                        }
                    }

                    if (!string.Equals(artistNameBeforeCorrect, lastFmArtistInfoLfm.Artist.Name, StringComparison.CurrentCultureIgnoreCase))
                    {
                        var aliasList = new List<ArtistAlias>
                        {
                            new ArtistAlias
                            {
                                Alias = artistNameBeforeCorrect,
                                CorrectsInScrobbles = true
                            }
                        };

                        artistToAdd.ArtistAliases = aliasList;
                    }

                    await db.Artists.AddAsync(artistToAdd);
                    await db.SaveChangesAsync();
                }
                else
                {
                    if (!string.Equals(artistNameBeforeCorrect, lastFmArtistInfoLfm.Artist.Name, StringComparison.CurrentCultureIgnoreCase))
                    {
                        AddAliasToExistingArtist(artistNameBeforeCorrect, dbArtist, db);
                    }

                    if (dbArtist.SpotifyImageUrl == null || dbArtist.SpotifyImageDate < DateTime.UtcNow.AddDays(-15))
                    {
                        var spotifyArtist = await GetArtistFromSpotify(lastFmArtistInfoLfm.Artist.Name);

                        if (spotifyArtist != null && spotifyArtist.Images.Any())
                        {
                            dbArtist.SpotifyImageUrl = spotifyArtist.Images.OrderByDescending(o => o.Height).First().Url;
                            imageUrlToReturn = dbArtist.SpotifyImageUrl;
                            dbArtist.Popularity = spotifyArtist.Popularity;
                        }

                        if (spotifyArtist != null && spotifyArtist.Genres.Any())
                        {
                            if (dbArtist.ArtistGenres != null && dbArtist.ArtistGenres.Any())
                            {
                                db.ArtistGenres.RemoveRange(dbArtist.ArtistGenres);
                            }

                            var genresToAdd = spotifyArtist.Genres.Select(s => new ArtistGenre
                            {
                                Artist = dbArtist,
                                Name = s
                            });

                            await db.ArtistGenres.AddRangeAsync(genresToAdd);
                        }

                        dbArtist.SpotifyImageDate = DateTime.UtcNow;
                        db.Entry(dbArtist).State = EntityState.Modified;
                    }
                    else
                    {
                        imageUrlToReturn = dbArtist.SpotifyImageUrl;
                    }

                    await db.SaveChangesAsync();
                }

                return !string.IsNullOrEmpty(imageUrlToReturn) ? imageUrlToReturn : null;
            }
            catch (Exception e)
            {
                Log.Error(e, "Something went wrong while retrieving artist image");
                return null;
            }
        }

        private static void AddAliasToExistingArtist(string artistNameBeforeCorrect, Artist dbArtist, FMBotDbContext db)
        {
            if (!dbArtist.ArtistAliases.Any() || !dbArtist.ArtistAliases.Select(s => s.Alias.ToLower()).Contains(artistNameBeforeCorrect.ToLower()))
            {
                db.ArtistAliases.Add(new ArtistAlias
                {
                    ArtistId = dbArtist.Id,
                    Alias = artistNameBeforeCorrect,
                    CorrectsInScrobbles = true
                });
            }
        }

        private static async Task<FullArtist> GetArtistFromSpotify(string artistName)
        {
            var spotify = GetSpotifyWebApi();

            var searchRequest = new SearchRequest(SearchRequest.Types.Artist, artistName);

            var results = await spotify.Search.Item(searchRequest);

            if (results.Artists.Items?.Any() == true)
            {
                var spotifyArtist = results.Artists.Items
                    .OrderByDescending(o => o.Popularity)
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
            await using var db = this._contextFactory.CreateDbContext();
            var dbTrack = await db.Tracks
                .Include(i => i.Artist)
                .AsQueryable()
                .FirstOrDefaultAsync(f => f.Name.ToLower() == trackInfo.TrackName.ToLower() && f.ArtistName.ToLower() == trackInfo.ArtistName.ToLower());

            if (dbTrack == null)
            {
                var trackToAdd = new Track
                {
                    Name = trackInfo.TrackName,
                    AlbumName = trackInfo.AlbumName,
                    ArtistName = trackInfo.ArtistName
                };

                var artist = await db.Artists
                    .AsQueryable()
                    .FirstOrDefaultAsync(f => f.Name.ToLower() == trackInfo.ArtistName.ToLower());

                if (artist != null)
                {
                    trackToAdd.Artist = artist;
                }

                var spotifyTrack = await GetTrackFromSpotify(trackInfo.TrackName, trackInfo.ArtistName.ToLower());

                if (spotifyTrack != null)
                {
                    trackToAdd.SpotifyId = spotifyTrack.Id;
                    trackToAdd.DurationMs = spotifyTrack.DurationMs;

                    var audioFeatures = await GetAudioFeaturesFromSpotify(spotifyTrack.Id);

                    if (audioFeatures != null)
                    {
                        trackToAdd.Key = audioFeatures.Key;
                        trackToAdd.Tempo = audioFeatures.Tempo;
                    }
                }

                trackToAdd.SpotifyLastUpdated = DateTime.UtcNow;

                await db.Tracks.AddAsync(trackToAdd);
                await db.SaveChangesAsync();

                return trackToAdd;
            }

            if (dbTrack.Artist == null)
            {
                var artist = await db.Artists
                    .AsQueryable()
                    .FirstOrDefaultAsync(f => f.Name.ToLower() == trackInfo.ArtistName.ToLower());

                if (artist != null)
                {
                    dbTrack.ArtistId = artist.Id;
                    db.Entry(dbTrack).State = EntityState.Modified;
                }
            }
            if (string.IsNullOrEmpty(dbTrack.SpotifyId) && dbTrack.SpotifyLastUpdated < DateTime.UtcNow.AddMonths(-2))
            {
                var spotifyTrack = await GetTrackFromSpotify(trackInfo.TrackName, trackInfo.ArtistName.ToLower());

                if (spotifyTrack != null)
                {
                    dbTrack.SpotifyId = spotifyTrack.Id;
                    dbTrack.DurationMs = spotifyTrack.DurationMs;

                    var audioFeatures = await GetAudioFeaturesFromSpotify(spotifyTrack.Id);

                    if (audioFeatures != null)
                    {
                        dbTrack.Key = audioFeatures.Key;
                        dbTrack.Tempo = audioFeatures.Tempo;
                    }
                }

                dbTrack.SpotifyLastUpdated = DateTime.UtcNow;
                db.Entry(dbTrack).State = EntityState.Modified;
            }

            await db.SaveChangesAsync();

            return dbTrack;
        }

        private static async Task<FullTrack> GetTrackFromSpotify(string trackName, string artistName)
        {
            //Create the auth object
            var spotify = GetSpotifyWebApi();

            var searchRequest = new SearchRequest(SearchRequest.Types.Track, $"track:{trackName} artist:{artistName}");

            var results = await spotify.Search.Item(searchRequest);

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

        private static async Task<FullAlbum> GetAlbumFromSpotify(string albumName, string artistName)
        {
            //Create the auth object
            var spotify = GetSpotifyWebApi();

            var searchRequest = new SearchRequest(SearchRequest.Types.Album, $"{albumName} {artistName}");

            var results = await spotify.Search.Item(searchRequest);

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

        public static async Task<FullTrack> GetTrackById(string spotifyId)
        {
            //Create the auth object
            var spotify = GetSpotifyWebApi();

            return await spotify.Tracks.Get(spotifyId);
        }

        public static async Task<FullAlbum> GetAlbumById(string spotifyId)
        {
            //Create the auth object
            var spotify = GetSpotifyWebApi();

            return await spotify.Albums.Get(spotifyId);
        }

        private static async Task<TrackAudioFeatures> GetAudioFeaturesFromSpotify(string spotifyId)
        {
            //Create the auth object
            var spotify = GetSpotifyWebApi();

            var result = await spotify.Tracks.GetAudioFeatures(spotifyId);

            return result;
        }

        public async Task<Album> GetOrStoreSpotifyAlbumAsync(AlbumInfo albumInfo)
        {
            await using var db = this._contextFactory.CreateDbContext();
            var dbAlbum = await db.Albums
                .Include(i => i.Artist)
                .Include(i => i.Tracks)
                .AsQueryable()
                .FirstOrDefaultAsync(f => f.Name.ToLower() == albumInfo.AlbumName.ToLower() && f.ArtistName.ToLower() == albumInfo.ArtistName.ToLower());

            if (dbAlbum == null)
            {
                var albumToAdd = new Album
                {
                    Name = albumInfo.AlbumName,
                    ArtistName = albumInfo.ArtistName,
                    LastFmUrl = albumInfo.AlbumUrl
                };

                var artist = await db.Artists
                    .AsQueryable()
                    .FirstOrDefaultAsync(f => f.Name.ToLower() == albumInfo.ArtistName.ToLower());

                if (artist != null)
                {
                    albumToAdd.Artist = artist;
                }

                var spotifyAlbum = await GetAlbumFromSpotify(albumInfo.AlbumName, albumInfo.ArtistName.ToLower());

                if (spotifyAlbum != null)
                {
                    albumToAdd.SpotifyId = spotifyAlbum.Id;
                    albumToAdd.Label = spotifyAlbum.Label;
                    albumToAdd.Popularity = spotifyAlbum.Popularity;
                    albumToAdd.SpotifyImageUrl = spotifyAlbum.Images.OrderByDescending(o => o.Height).First().Url;

                    var dbTracks = await GetOrStoreDbTracks(spotifyAlbum.Tracks.Items, albumInfo, albumToAdd, db);
                    if (dbTracks.Any())
                    {
                        albumToAdd.Tracks = dbTracks;
                    }
                }

                albumToAdd.SpotifyImageDate = DateTime.UtcNow;

                await db.Albums.AddAsync(albumToAdd);
                await db.SaveChangesAsync();

                return albumToAdd;
            }
            if (dbAlbum.Artist == null)
            {
                var artist = await db.Artists
                    .AsQueryable()
                    .FirstOrDefaultAsync(f => f.Name.ToLower() == albumInfo.ArtistName.ToLower());

                if (artist != null)
                {
                    dbAlbum.ArtistId = artist.Id;
                    db.Entry(dbAlbum).State = EntityState.Modified;
                    await db.SaveChangesAsync();
                }
            }
            if (string.IsNullOrEmpty(dbAlbum.SpotifyId) && dbAlbum.SpotifyImageDate < DateTime.UtcNow.AddMonths(-2))
            {
                var spotifyAlbum = await GetAlbumFromSpotify(albumInfo.AlbumName, albumInfo.ArtistName.ToLower());

                if (spotifyAlbum != null)
                {
                    dbAlbum.SpotifyId = spotifyAlbum.Id;
                    dbAlbum.Label = spotifyAlbum.Label;
                    dbAlbum.Popularity = spotifyAlbum.Popularity;
                    dbAlbum.SpotifyImageUrl = spotifyAlbum.Images.OrderByDescending(o => o.Height).First().Url;

                    var dbTracks = await GetOrStoreDbTracks(spotifyAlbum.Tracks.Items, albumInfo, dbAlbum, db);
                    if (dbTracks.Any())
                    {
                        dbAlbum.Tracks = dbTracks;
                    }
                }

                dbAlbum.SpotifyImageDate = DateTime.UtcNow;

                db.Entry(dbAlbum).State = EntityState.Modified;

                await db.SaveChangesAsync();
            }

            return dbAlbum;
        }

        private async Task<List<Track>> GetOrStoreDbTracks(List<SimpleTrack> simpleTracks, AlbumInfo albumInfo,
            Album album, FMBotDbContext fmBotDbContext)
        {
            var dbTracks = new List<Track>();
            foreach (var track in simpleTracks.OrderBy(o => o.TrackNumber))
            {
                var dbTrack = await fmBotDbContext.Tracks
                    .AsQueryable()
                    .FirstOrDefaultAsync(f => f.Name.ToLower() == track.Name.ToLower() &&
                                                                       f.ArtistName.ToLower() == albumInfo.ArtistName.ToLower());

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
                        SpotifyLastUpdated = DateTime.UtcNow,
                        Album = album
                    };

                    await fmBotDbContext.Tracks.AddAsync(trackToAdd);
                    dbTracks.Add(trackToAdd);
                }
            }

            return dbTracks;
        }

        private static SpotifyClient GetSpotifyWebApi()
        {
            var config = SpotifyClientConfig
                .CreateDefault()
                .WithAuthenticator(new ClientCredentialsAuthenticator(ConfigData.Data.Spotify.Key, ConfigData.Data.Spotify.Secret));

            return new SpotifyClient(config);
        }

        public static RecentTrack SpotifyGameToRecentTrack(SpotifyGame spotifyActivity)
        {
            return new()
            {
                TrackName = spotifyActivity.TrackTitle,
                AlbumName = spotifyActivity.AlbumTitle,
                ArtistName = spotifyActivity.Artists.First(),
                AlbumCoverUrl = spotifyActivity.AlbumArtUrl,
                TrackUrl = spotifyActivity.TrackUrl
            };
        }
    }
}
