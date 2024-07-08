using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FMBot.AppleMusic.Models;
using FMBot.Bot.Services;
using FMBot.Bot.Services.ThirdParty;
using FMBot.Domain.Enums;
using FMBot.Domain.Models;
using FMBot.Persistence.Domain.Models;
using FMBot.Persistence.EntityFrameWork;
using FMBot.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using Serilog;
using SpotifyAPI.Web;
using Web.InternalApi;
using static FMBot.Bot.Services.MusicBrainzService;

namespace FMBot.Bot.Factories;

public class MusicDataFactory
{
    private readonly IDbContextFactory<FMBotDbContext> _contextFactory;
    private readonly SpotifyService _spotifyService;
    private readonly ArtistEnrichment.ArtistEnrichmentClient _artistEnrichment;
    private readonly AlbumEnrichment.AlbumEnrichmentClient _albumEnrichment;
    private readonly MusicBrainzService _musicBrainzService;
    private readonly BotSettings _botSettings;
    private readonly AppleMusicService _appleMusicService;

    public MusicDataFactory(IOptions<BotSettings> botSettings, SpotifyService spotifyService, ArtistEnrichment.ArtistEnrichmentClient artistEnrichment, IDbContextFactory<FMBotDbContext> contextFactory, MusicBrainzService musicBrainzService, AppleMusicService appleMusicService, AlbumEnrichment.AlbumEnrichmentClient albumEnrichment)
    {
        this._spotifyService = spotifyService;
        this._artistEnrichment = artistEnrichment;
        this._contextFactory = contextFactory;
        this._musicBrainzService = musicBrainzService;
        this._appleMusicService = appleMusicService;
        this._albumEnrichment = albumEnrichment;
        this._botSettings = botSettings.Value;
    }

    public async Task<Artist> GetOrStoreArtistAsync(ArtistInfo artistInfo, string artistNameBeforeCorrect = null, bool redirectsEnabled = true)
    {
        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        try
        {
            var dbArtist = await ArtistRepository.GetArtistForName(artistInfo.ArtistName, connection, true, true, true);

            if (dbArtist == null)
            {
                await using var db = await this._contextFactory.CreateDbContextAsync();

                var artistToAdd = new Artist
                {
                    Name = artistInfo.ArtistName,
                    LastFmUrl = artistInfo.ArtistUrl,
                    Mbid = artistInfo.Mbid,
                    LastFmDescription = artistInfo.Description,
                    LastfmDate = DateTime.UtcNow
                };

                var spotifyArtistTask = this._spotifyService.GetArtistFromSpotify(artistInfo.ArtistName);
                var musicBrainzUpdatedTask = this._musicBrainzService.AddMusicBrainzDataToArtistAsync(artistToAdd);
                var appleMusicArtistTask = this._appleMusicService.GetAppleMusicArtist(artistInfo.ArtistName);

                var spotifyArtist = await spotifyArtistTask;
                var musicBrainzUpdated = await musicBrainzUpdatedTask;
                var amArtist = await appleMusicArtistTask;

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
                            await this._artistEnrichment.AddArtistImageToCacheAsync(new AddedArtistImage
                            {
                                ArtistName = artistInfo.ArtistName,
                                ArtistImageUrl = artistToAdd.SpotifyImageUrl
                            });
                        }
                    }

                    await db.Artists.AddAsync(artistToAdd);
                    await db.SaveChangesAsync();

                    if (spotifyArtist.Images.Any())
                    {
                        var img = spotifyArtist.Images.OrderByDescending(o => o.Height).First();
                        await AddOrUpdateArtistImage(db, artistToAdd.Id, ImageSource.Spotify, img.Url, img.Height, img.Width);
                    }

                    if (spotifyArtist.Genres.Any())
                    {
                        await ArtistRepository.AddOrUpdateArtistGenres(artistToAdd.Id, spotifyArtist.Genres.Select(s => s), connection);
                    }
                }
                else
                {
                    await db.Artists.AddAsync(artistToAdd);
                    await db.SaveChangesAsync();
                }

                if (musicBrainzUpdated.Updated && artistToAdd.ArtistLinks != null && artistToAdd.ArtistLinks.Count != 0 && artistToAdd.Id != 0)
                {
                    await ArtistRepository.AddOrUpdateArtistLinks(artistToAdd.Id, artistToAdd.ArtistLinks, connection);
                }

                if (spotifyArtist != null && spotifyArtist.Genres.Any())
                {
                    artistToAdd.ArtistGenres = spotifyArtist.Genres.Select(s => new ArtistGenre
                    {
                        Name = s
                    }).ToList();
                }

                if (redirectsEnabled &&
                    artistNameBeforeCorrect != null &&
                    !string.Equals(artistNameBeforeCorrect, artistInfo.ArtistName, StringComparison.OrdinalIgnoreCase))
                {
                    await ArtistRepository.AddOrUpdateArtistAlias(artistToAdd.Id, artistNameBeforeCorrect, connection);
                }

                if (amArtist != null)
                {
                    artistToAdd.AppleMusicId = amArtist.Id;
                    artistToAdd.AppleMusicUrl = amArtist.Attributes.Url;

                    if (amArtist.Attributes?.Artwork?.Url != null)
                    {
                        await AddOrUpdateArtistImage(db, artistToAdd.Id, ImageSource.AppleMusic, amArtist.Attributes.Artwork?.Url,
                            amArtist.Attributes.Artwork.Height, amArtist.Attributes.Artwork.Width, amArtist.Attributes.Artwork);
                    }

                    artistToAdd.AppleMusicDate = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc);
                    db.Entry(artistToAdd).State = EntityState.Modified;
                    await db.SaveChangesAsync();
                }

                return artistToAdd;
            }

            Task<FullArtist> updateSpotify = null;
            Task<ArtistUpdated> updateMusicBrainz = null;
            Task<AmData<AmArtistAttributes>> updateAppleMusic = null;

            if (dbArtist.SpotifyImageUrl == null || dbArtist.SpotifyImageDate < DateTime.UtcNow.AddDays(-60))
            {
                updateSpotify = this._spotifyService.GetArtistFromSpotify(artistInfo.ArtistName);
            }
            if (dbArtist.MusicBrainzDate == null || dbArtist.MusicBrainzDate < DateTime.UtcNow.AddDays(-120))
            {
                updateMusicBrainz = this._musicBrainzService.AddMusicBrainzDataToArtistAsync(dbArtist);
            }
            if (dbArtist.AppleMusicDate == null || dbArtist.AppleMusicDate < DateTime.UtcNow.AddDays(-120))
            {
                updateAppleMusic = this._appleMusicService.GetAppleMusicArtist(artistInfo.ArtistName);
            }

            if (redirectsEnabled &&
                artistNameBeforeCorrect != null &&
                !string.Equals(artistNameBeforeCorrect, artistInfo.ArtistName, StringComparison.OrdinalIgnoreCase))
            {
                await ArtistRepository.AddOrUpdateArtistAlias(dbArtist.Id, artistNameBeforeCorrect, connection);
            }

            if (artistInfo.Description != null && dbArtist.LastFmDescription != artistInfo.Description)
            {
                await using var db = await this._contextFactory.CreateDbContextAsync();

                dbArtist.LastFmDescription = artistInfo.Description;
                dbArtist.LastfmDate = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc);
                db.Entry(dbArtist).State = EntityState.Modified;

                await db.SaveChangesAsync();
            }

            if (updateMusicBrainz != null)
            {
                var musicBrainzUpdate = await updateMusicBrainz;

                if (musicBrainzUpdate.Updated)
                {
                    dbArtist = musicBrainzUpdate.Artist;

                    if (dbArtist.ArtistLinks != null && dbArtist.ArtistLinks.Any())
                    {
                        await ArtistRepository.AddOrUpdateArtistLinks(dbArtist.Id, dbArtist.ArtistLinks, connection);
                    }

                    await using var db = await this._contextFactory.CreateDbContextAsync();
                    db.Entry(dbArtist).State = EntityState.Modified;
                    await db.SaveChangesAsync();
                }
            }

            if (updateSpotify != null)
            {
                await using var db = await this._contextFactory.CreateDbContextAsync();

                var spotifyArtist = await updateSpotify;

                if (spotifyArtist != null && spotifyArtist.Images.Any())
                {
                    var newImage = spotifyArtist.Images.OrderByDescending(o => o.Height).First();
                    dbArtist.SpotifyImageUrl = newImage.Url;

                    dbArtist.SpotifyId = spotifyArtist.Id;
                    dbArtist.Popularity = spotifyArtist.Popularity;

                    if (artistInfo.ArtistUrl != null)
                    {
                        await this._artistEnrichment.AddArtistImageToCacheAsync(new AddedArtistImage
                        {
                            ArtistName = artistInfo.ArtistName,
                            ArtistImageUrl = dbArtist.SpotifyImageUrl
                        });
                    }

                    await AddOrUpdateArtistImage(db, dbArtist.Id, ImageSource.Spotify, newImage.Url, newImage.Height, newImage.Width);
                }

                if (spotifyArtist != null && spotifyArtist.Genres.Any())
                {
                    await ArtistRepository.AddOrUpdateArtistGenres(dbArtist.Id, spotifyArtist.Genres.Select(s => s), connection);
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

            if (updateAppleMusic != null)
            {
                await using var db = await this._contextFactory.CreateDbContextAsync();
                var amArtist = await updateAppleMusic;

                if (amArtist != null)
                {
                    dbArtist.AppleMusicId = amArtist.Id;
                    dbArtist.AppleMusicUrl = amArtist.Attributes.Url;

                    if (amArtist.Attributes.Artwork?.Url != null)
                    {
                        await AddOrUpdateArtistImage(db, dbArtist.Id, ImageSource.AppleMusic, amArtist.Attributes.Artwork?.Url,
                            amArtist.Attributes.Artwork.Height, amArtist.Attributes.Artwork.Width, amArtist.Attributes.Artwork);
                    }
                }

                dbArtist.AppleMusicDate = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc);
                db.Entry(dbArtist).State = EntityState.Modified;
                await db.SaveChangesAsync();
            }

            await connection.CloseAsync();
            return dbArtist;
        }
        catch (Exception e)
        {
            Log.Error(e, $"{nameof(MusicDataFactory)}: Something went wrong while retrieving artist data");
            return new Artist
            {
                Name = artistInfo.ArtistName,
                LastFmUrl = artistInfo.ArtistUrl
            };
        }
    }

    private static async Task AddOrUpdateArtistImage(FMBotDbContext db, int artistId, ImageSource imageSource,
        string imageUrl, int height, int width, AmArtwork artworkDetails = null)
    {
        var existingImages = await db.ArtistImages
            .Where(w => w.ArtistId == artistId)
            .ToListAsync();

        var existingImage = existingImages.FirstOrDefault(f => f.ImageSource == imageSource);
        if (existingImage != null)
        {
            existingImage.Url = imageUrl;
            existingImage.LastUpdated = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc);

            existingImage.Width = width;
            existingImage.Height = height;

            if (artworkDetails != null)
            {
                existingImage.BgColor = artworkDetails.BgColor;
                existingImage.TextColor1 = artworkDetails.TextColor1;
                existingImage.TextColor2 = artworkDetails.TextColor2;
                existingImage.TextColor3 = artworkDetails.TextColor3;
                existingImage.TextColor4 = artworkDetails.TextColor4;
            }

            db.ArtistImages.Update(existingImage);
        }
        else
        {
            await db.ArtistImages.AddAsync(new ArtistImage
            {
                ArtistId = artistId,
                ImageSource = imageSource,
                Width = width,
                Height = height,
                LastUpdated = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc),
                Url = imageUrl,
                BgColor = artworkDetails?.BgColor,
                TextColor1 = artworkDetails?.TextColor1,
                TextColor2 = artworkDetails?.TextColor2,
                TextColor3 = artworkDetails?.TextColor3,
                TextColor4 = artworkDetails?.TextColor4
            });
        }

        await db.SaveChangesAsync();
    }

    public async Task<Album> GetOrStoreAlbumAsync(AlbumInfo albumInfo)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();

        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        var dbAlbum = await AlbumRepository.GetAlbumForName(albumInfo.ArtistName, albumInfo.AlbumName, connection);

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

            var artist = await ArtistRepository.GetArtistForName(albumInfo.ArtistName, connection);

            if (artist != null && artist.Id != 0)
            {
                albumToAdd.ArtistId = artist.Id;
            }

            var spotifyAlbumTask = this._spotifyService.GetAlbumFromSpotify(albumInfo.AlbumName, albumInfo.ArtistName.ToLower());
            var amAlbumTask = this._appleMusicService.GetAppleMusicAlbum(albumInfo.ArtistName, albumInfo.AlbumName);

            var spotifyAlbum = await spotifyAlbumTask;
            var amAlbum = await amAlbumTask;

            if (spotifyAlbum != null)
            {
                albumToAdd.SpotifyId = spotifyAlbum.Id;
                albumToAdd.Label = spotifyAlbum.Label;
                albumToAdd.Popularity = spotifyAlbum.Popularity;
                albumToAdd.SpotifyImageUrl = spotifyAlbum.Images.OrderByDescending(o => o.Height).First().Url;
                albumToAdd.ReleaseDate = spotifyAlbum.ReleaseDate;
                albumToAdd.ReleaseDatePrecision = spotifyAlbum.ReleaseDatePrecision;

                var spotifyUpc = spotifyAlbum.ExternalIds.FirstOrDefault(f => f.Key == "upc");
                albumToAdd.Upc = spotifyUpc.Value;
            }

            if (amAlbum != null)
            {
                albumToAdd.AppleMusicUrl = amAlbum.Attributes.Url;
                albumToAdd.AppleMusicId = amAlbum.Id;
                albumToAdd.AppleMusicTagline = amAlbum.Attributes.EditorialNotes?.Tagline;
                albumToAdd.AppleMusicDescription = amAlbum.Attributes.EditorialNotes?.Standard;
                albumToAdd.AppleMusicShortDescription = amAlbum.Attributes.EditorialNotes?.Short;
                albumToAdd.Upc = amAlbum.Attributes.Upc;

                if (albumToAdd.ReleaseDate == null && amAlbum.Attributes.ReleaseDate != null)
                {
                    albumToAdd.ReleaseDate = amAlbum.Attributes.ReleaseDate;
                    albumToAdd.ReleaseDatePrecision = amAlbum.Attributes.ReleaseDate.Length == 4 ? "year" : "day";
                }
            }

            var coverUrl = albumInfo.AlbumCoverUrl ?? albumToAdd.SpotifyImageUrl;
            if (coverUrl != null && albumInfo.AlbumUrl != null)
            {
                await this._albumEnrichment.AddAlbumCoverToCacheAsync(new AddedAlbumCover
                {
                    ArtistName = albumInfo.ArtistName,
                    AlbumName = albumInfo.AlbumName,
                    AlbumCoverUrl = coverUrl
                });
            }

            albumToAdd.SpotifyImageDate = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc);
            albumToAdd.AppleMusicDate = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc);

            await db.Albums.AddAsync(albumToAdd);
            await db.SaveChangesAsync();

            if (spotifyAlbum != null)
            {
                await GetOrStoreAlbumTracks(spotifyAlbum.Tracks.Items, albumInfo, albumToAdd.Id, connection);

                var img = spotifyAlbum.Images.OrderByDescending(o => o.Height).First();
                await AddOrUpdateAlbumImage(db, albumToAdd.Id, ImageSource.Spotify, img.Url, img.Height, img.Width);
            }

            if (amAlbum?.Attributes.Artwork?.Url != null)
            {
                await AddOrUpdateAlbumImage(db, albumToAdd.Id, ImageSource.AppleMusic, amAlbum.Attributes.Artwork.Url,
                    amAlbum.Attributes.Artwork.Height, amAlbum.Attributes.Artwork.Width, amAlbum.Attributes.Artwork);
            }

            await connection.CloseAsync();

            return albumToAdd;
        }
        if (albumInfo.AlbumCoverUrl != null &&
            !albumInfo.AlbumCoverUrl.Contains("i.scdn.co"))
        {
            await AddOrUpdateAlbumImage(db, dbAlbum.Id, ImageSource.LastFm, albumInfo.AlbumCoverUrl, null, null);

            dbAlbum.LastfmImageUrl = albumInfo.AlbumCoverUrl;
            dbAlbum.LastfmDate = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc);
            db.Entry(dbAlbum).State = EntityState.Modified;
            await db.SaveChangesAsync();
        }
        if (albumInfo.Description != null && dbAlbum.LastFmDescription != albumInfo.Description)
        {
            dbAlbum.LastFmDescription = albumInfo.Description;
            dbAlbum.LastfmDate = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc);
            db.Entry(dbAlbum).State = EntityState.Modified;
        }

        if (!dbAlbum.ArtistId.HasValue)
        {
            var artist = await ArtistRepository.GetArtistForName(albumInfo.ArtistName, connection);

            if (artist != null && artist.Id != 0)
            {
                dbAlbum.ArtistId = artist.Id;
                db.Entry(dbAlbum).State = EntityState.Modified;
                await db.SaveChangesAsync();
            }
        }

        Task<FullAlbum> updateSpotify = null;
        Task<AmData<AmAlbumAttributes>> updateAppleMusic = null;

        if (dbAlbum.SpotifyImageDate == null || dbAlbum.SpotifyImageDate < DateTime.UtcNow.AddDays(-60))
        {
            updateSpotify = this._spotifyService.GetAlbumFromSpotify(albumInfo.AlbumName, albumInfo.ArtistName.ToLower());
        }
        if (dbAlbum.AppleMusicDate == null || dbAlbum.AppleMusicDate < DateTime.UtcNow.AddDays(-120))
        {
            updateAppleMusic =
                this._appleMusicService.GetAppleMusicAlbum(albumInfo.ArtistName, albumInfo.AlbumName);
        }

        if (updateSpotify != null)
        {
            var spotifyAlbum = await updateSpotify;
            dbAlbum.SpotifyImageDate = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc);

            if (spotifyAlbum != null)
            {
                var img = spotifyAlbum.Images.OrderByDescending(o => o.Height).First();

                dbAlbum.SpotifyId = spotifyAlbum.Id;
                dbAlbum.Label = spotifyAlbum.Label;
                dbAlbum.Popularity = spotifyAlbum.Popularity;
                dbAlbum.SpotifyImageUrl = img.Url;
                dbAlbum.ReleaseDate = spotifyAlbum.ReleaseDate;
                dbAlbum.ReleaseDatePrecision = spotifyAlbum.ReleaseDatePrecision;

                await AddOrUpdateAlbumImage(db, dbAlbum.Id, ImageSource.Spotify, img.Url, img.Height, img.Width);

                await GetOrStoreAlbumTracks(spotifyAlbum.Tracks.Items, albumInfo, dbAlbum.Id, connection);
            }

            var coverUrl = albumInfo.AlbumCoverUrl ?? dbAlbum.SpotifyImageUrl;
            if (coverUrl != null && albumInfo.AlbumUrl != null)
            {
                await this._albumEnrichment.AddAlbumCoverToCacheAsync(new AddedAlbumCover
                {
                    ArtistName = albumInfo.ArtistName,
                    AlbumName = albumInfo.AlbumName,
                    AlbumCoverUrl = coverUrl
                });
            }

            db.Entry(dbAlbum).State = EntityState.Modified;
            await db.SaveChangesAsync();
        }

        if (updateAppleMusic != null)
        {
            var amAlbum = await updateAppleMusic;
            dbAlbum.AppleMusicDate = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc);

            if (amAlbum != null)
            {
                dbAlbum.AppleMusicUrl = amAlbum.Attributes.Url;
                dbAlbum.AppleMusicId = amAlbum.Id;
                dbAlbum.AppleMusicDescription = amAlbum.Attributes.EditorialNotes?.Standard;
                dbAlbum.AppleMusicShortDescription = amAlbum.Attributes.EditorialNotes?.Short;
                dbAlbum.AppleMusicTagline = amAlbum.Attributes.EditorialNotes?.Tagline;
                dbAlbum.Upc = amAlbum.Attributes.Upc;

                if (amAlbum.Attributes.Artwork?.Url != null)
                {
                    await AddOrUpdateAlbumImage(db, dbAlbum.Id, ImageSource.AppleMusic, amAlbum.Attributes.Artwork.Url,
                        amAlbum.Attributes.Artwork.Height, amAlbum.Attributes.Artwork.Width, amAlbum.Attributes.Artwork);
                }

                if (dbAlbum.ReleaseDate == null && amAlbum.Attributes.ReleaseDate != null)
                {
                    dbAlbum.ReleaseDate = amAlbum.Attributes.ReleaseDate;
                    dbAlbum.ReleaseDatePrecision = amAlbum.Attributes.ReleaseDate.Length == 4 ? "year" : "day";
                }
            }

            db.Entry(dbAlbum).State = EntityState.Modified;
            await db.SaveChangesAsync();
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

    private static async Task AddOrUpdateAlbumImage(FMBotDbContext db, int albumId, ImageSource imageSource,
        string imageUrl, int? height, int? width, AmArtwork artworkDetails = null)
    {
        var existingImages = await db.AlbumImages
            .Where(w => w.AlbumId == albumId)
            .ToListAsync();

        var existingImage = existingImages.FirstOrDefault(f => f.ImageSource == imageSource);
        if (existingImage != null)
        {
            existingImage.Url = imageUrl;
            existingImage.LastUpdated = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc);

            existingImage.Width = width;
            existingImage.Height = height;

            if (artworkDetails != null)
            {
                existingImage.BgColor = artworkDetails.BgColor;
                existingImage.TextColor1 = artworkDetails.TextColor1;
                existingImage.TextColor2 = artworkDetails.TextColor2;
                existingImage.TextColor3 = artworkDetails.TextColor3;
                existingImage.TextColor4 = artworkDetails.TextColor4;
            }

            db.AlbumImages.Update(existingImage);
        }
        else
        {
            await db.AlbumImages.AddAsync(new AlbumImage
            {
                AlbumId = albumId,
                ImageSource = imageSource,
                Width = width,
                Height = height,
                LastUpdated = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc),
                Url = imageUrl,
                BgColor = artworkDetails?.BgColor,
                TextColor1 = artworkDetails?.TextColor1,
                TextColor2 = artworkDetails?.TextColor2,
                TextColor3 = artworkDetails?.TextColor3,
                TextColor4 = artworkDetails?.TextColor4
            });
        }

        await db.SaveChangesAsync();
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
                    DurationMs = (int?)trackInfo.Duration,
                    LastFmUrl = trackInfo.TrackUrl,
                    LastFmDescription = trackInfo.Description,
                    LastfmDate = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc)
                };

                var artist = await ArtistRepository.GetArtistForName(trackInfo.ArtistName, connection);

                if (artist != null)
                {
                    trackToAdd.ArtistId = artist.Id;
                }

                var spotifyTrackTask = this._spotifyService.GetTrackFromSpotify(trackInfo.TrackName, trackInfo.ArtistName.ToLower());
                var amSongTask =
                    this._appleMusicService.GetAppleMusicSong(trackInfo.ArtistName, trackInfo.TrackName);

                var spotifyTrack = await spotifyTrackTask;
                var amSong = await amSongTask;

                if (spotifyTrack != null)
                {
                    trackToAdd.SpotifyId = spotifyTrack.Id;
                    trackToAdd.DurationMs = spotifyTrack.DurationMs;
                    trackToAdd.Popularity = spotifyTrack.Popularity;
                    trackToAdd.SpotifyPreviewUrl = spotifyTrack.PreviewUrl;

                    var audioFeatures = await this._spotifyService.GetAudioFeaturesFromSpotify(spotifyTrack.Id);

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

                if (amSong != null)
                {
                    trackToAdd.AppleMusicUrl = amSong.Attributes.Url;
                    trackToAdd.AppleMusicId = amSong.Id;
                    trackToAdd.AppleMusicTagline = amSong.Attributes.EditorialNotes?.Tagline;
                    trackToAdd.AppleMusicDescription = amSong.Attributes.EditorialNotes?.Standard;
                    trackToAdd.AppleMusicShortDescription = amSong.Attributes.EditorialNotes?.Short;
                    trackToAdd.Isrc = amSong.Attributes.Isrc;
                    trackToAdd.AppleMusicPreviewUrl = amSong.Attributes.Previews?.FirstOrDefault()?.Url;
                    if (!trackToAdd.DurationMs.HasValue && amSong.Attributes.DurationInMillis != 0)
                    {
                        trackToAdd.DurationMs = amSong.Attributes.DurationInMillis;
                    }
                }

                trackToAdd.SpotifyLastUpdated = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc);
                trackToAdd.AppleMusicDate = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc);

            await db.Tracks.AddAsync(trackToAdd);
                await db.SaveChangesAsync();

                return trackToAdd;
            }
            if (!dbTrack.ArtistId.HasValue)
            {
                var artist = await ArtistRepository.GetArtistForName(trackInfo.ArtistName, connection);

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

            Task<FullTrack> updateSpotify = null;
            Task<AmData<AmSongAttributes>> updateAppleMusic = null;

            if (dbTrack.SpotifyLastUpdated == null || dbTrack.SpotifyLastUpdated < DateTime.UtcNow.AddDays(-120))
            {
                updateSpotify = this._spotifyService.GetTrackFromSpotify(trackInfo.TrackName, trackInfo.ArtistName.ToLower());
            }
            if (dbTrack.AppleMusicDate == null || dbTrack.AppleMusicDate < DateTime.UtcNow.AddDays(-120))
            {
                updateAppleMusic =
                    this._appleMusicService.GetAppleMusicSong(trackInfo.ArtistName, trackInfo.TrackName);
            }

            if (updateSpotify != null)
            {
                var spotifyTrack = await updateSpotify;

                if (spotifyTrack != null)
                {
                    dbTrack.SpotifyId = spotifyTrack.Id;
                    dbTrack.DurationMs = spotifyTrack.DurationMs;
                    dbTrack.Popularity = spotifyTrack.Popularity;
                    dbTrack.SpotifyPreviewUrl = spotifyTrack.PreviewUrl;

                    var audioFeatures = await this._spotifyService.GetAudioFeaturesFromSpotify(spotifyTrack.Id);

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

            if (updateAppleMusic != null)
            {
                var amSong = await updateAppleMusic;

                if (amSong != null)
                {
                    dbTrack.AppleMusicUrl = amSong.Attributes.Url;
                    dbTrack.AppleMusicId = amSong.Id;
                    dbTrack.AppleMusicTagline = amSong.Attributes.EditorialNotes?.Tagline;
                    dbTrack.AppleMusicDescription = amSong.Attributes.EditorialNotes?.Standard;
                    dbTrack.AppleMusicShortDescription = amSong.Attributes.EditorialNotes?.Short;
                    dbTrack.Isrc = amSong.Attributes.Isrc;
                    dbTrack.AppleMusicPreviewUrl = amSong.Attributes.Previews?.FirstOrDefault()?.Url;

                    if (!dbTrack.DurationMs.HasValue && amSong.Attributes.DurationInMillis != 0)
                    {
                        dbTrack.DurationMs = amSong.Attributes.DurationInMillis;
                    }
                }

                dbTrack.AppleMusicDate = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc);
                db.Entry(dbTrack).State = EntityState.Modified;
            }

            await db.SaveChangesAsync();

            await connection.CloseAsync();

            return dbTrack;
        }
        catch (Exception e)
        {
            Log.Error(e, $"{nameof(MusicDataFactory)}: Something went wrong while retrieving track info");
            return null;
        }
    }
}
