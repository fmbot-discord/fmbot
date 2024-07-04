using System;
using System.Linq;
using System.Threading.Tasks;
using FMBot.AppleMusic.Models;
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

namespace FMBot.Bot.Services;

public class MusicDataService
{
    private readonly IDbContextFactory<FMBotDbContext> _contextFactory;
    private readonly SpotifyService _spotifyService;
    private readonly ArtistEnrichment.ArtistEnrichmentClient _artistEnrichment;
    private readonly MusicBrainzService _musicBrainzService;
    private readonly BotSettings _botSettings;
    private readonly AppleMusicService _appleMusicService;

    public MusicDataService(IOptions<BotSettings> botSettings, SpotifyService spotifyService, ArtistEnrichment.ArtistEnrichmentClient artistEnrichment, IDbContextFactory<FMBotDbContext> contextFactory, MusicBrainzService musicBrainzService, AppleMusicService appleMusicService)
    {
        this._spotifyService = spotifyService;
        this._artistEnrichment = artistEnrichment;
        this._contextFactory = contextFactory;
        this._musicBrainzService = musicBrainzService;
        this._appleMusicService = appleMusicService;
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
                var spotifyArtist = await this._spotifyService.GetArtistFromSpotify(artistInfo.ArtistName);

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

                return artistToAdd;
            }

            Task<FullArtist> updateSpotify = null;
            Task<ArtistUpdated> updateMusicBrainz = null;
            Task<AmData<AmArtistAttributes>> updateAppleMusic = null;

            if (dbArtist.SpotifyImageUrl == null || dbArtist.SpotifyImageDate < DateTime.UtcNow.AddDays(-25))
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
                var musicBrainzUpdate = await this._musicBrainzService.AddMusicBrainzDataToArtistAsync(dbArtist);

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
            Log.Error(e, "Something went wrong while retrieving artist image");
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
        var existingImages = await db
            .ArtistImages
            .Where(w => w.ArtistId == artistId).ToListAsync();

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
}
