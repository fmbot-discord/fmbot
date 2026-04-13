using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using FMBot.AppleMusic;
using FMBot.AppleMusic.Models;
using FMBot.Domain;
using FMBot.Persistence.Domain.Models;
using FMBot.Persistence.EntityFrameWork;
using Microsoft.EntityFrameworkCore;

namespace FMBot.Bot.Services.ThirdParty;

public class AppleMusicService
{
    private static readonly ActivitySource ActivitySource = new("FMBot.AppleMusic");

    private readonly AppleMusicApi _appleMusicApi;
    private readonly AppleMusicAltApi _appleMusicAltApi;
    private readonly IDbContextFactory<FMBotDbContext> _contextFactory;

    public AppleMusicService(AppleMusicApi appleMusicApi, AppleMusicAltApi appleMusicAltApi,
        IDbContextFactory<FMBotDbContext> contextFactory)
    {
        this._appleMusicApi = appleMusicApi;
        this._appleMusicAltApi = appleMusicAltApi;
        this._contextFactory = contextFactory;
    }

    public async Task<AmData<AmArtistAttributes>> GetAppleMusicArtist(string artist)
    {
        using var activity = ActivitySource.StartActivity("GetArtist");

        var results = await this._appleMusicApi.SearchArtistAsync(artist);
        Statistics.AppleMusicApiCalls.Inc();

        return results?.FirstOrDefault(f => f?.Attributes?.Name != null &&
                                            f.Attributes.Name.Equals(artist, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<AmData<AmAlbumAttributes>> GetAppleMusicAlbum(string artist, string albumName,
        bool getMotionCovers = false)
    {
        using var activity = ActivitySource.StartActivity("GetAlbum");

        var results = await this._appleMusicApi.SearchAlbumAsync($"{artist} - {albumName}");
        Statistics.AppleMusicApiCalls.Inc();

        var result = results?.FirstOrDefault(f => f?.Attributes?.Name != null &&
                                                  f.Attributes.Name.Contains(albumName,
                                                      StringComparison.OrdinalIgnoreCase) &&
                                                  f.Attributes.ArtistName.Contains(artist,
                                                      StringComparison.OrdinalIgnoreCase));

        if (result == null)
        {
            return null;
        }

        if (getMotionCovers)
        {
            var altCall = await this._appleMusicAltApi.GetAlbumAsync(result.Id.ToString());
            var editorialVideo = altCall?.Data.FirstOrDefault()?.Attributes?.EditorialVideo;
            if (editorialVideo != null)
            {
                result.Attributes.EditorialVideo = editorialVideo;
            }
        }

        return result;
    }

    public async Task<AmData<AmSongAttributes>> GetAppleMusicSong(string artist, string songName)
    {
        using var activity = ActivitySource.StartActivity("GetSong");

        var results = await this._appleMusicApi.SearchSongAsync($"{artist} - {songName}");
        Statistics.AppleMusicApiCalls.Inc();

        return results?.FirstOrDefault(f => f?.Attributes?.Name != null &&
                                            f.Attributes.Name.Contains(songName, StringComparison.OrdinalIgnoreCase) &&
                                            f.Attributes.ArtistName.Contains(artist,
                                                StringComparison.OrdinalIgnoreCase));
    }

    public async Task<AmData<AmSongAttributes>> SearchAppleMusicSong(string searchQuery)
    {
        var results = await this._appleMusicApi.SearchSongAsync(searchQuery);
        Statistics.AppleMusicApiCalls.Inc();

        return results?.FirstOrDefault();
    }

    public async Task<Track> GetTrackForAppleMusicId(int appleMusicId)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        return await db.Tracks.FirstOrDefaultAsync(f => f.AppleMusicId == appleMusicId);
    }

    public async Task<Album> GetAlbumForAppleMusicId(int appleMusicId)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        return await db.Albums.FirstOrDefaultAsync(f => f.AppleMusicId == appleMusicId);
    }

    public async Task<Artist> GetArtistForAppleMusicId(int appleMusicId)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        return await db.Artists.FirstOrDefaultAsync(f => f.AppleMusicId == appleMusicId);
    }

    public async Task<AmData<AmSongAttributes>> GetAppleMusicSongById(string songId)
    {
        var result = await this._appleMusicApi.GetSongAsync(songId);
        Statistics.AppleMusicApiCalls.Inc();

        return result?.Data?.FirstOrDefault();
    }

    public async Task<AmData<AmAlbumAttributes>> GetAppleMusicAlbumById(string albumId)
    {
        var result = await this._appleMusicApi.GetAlbumAsync(albumId);
        Statistics.AppleMusicApiCalls.Inc();

        return result?.Data?.FirstOrDefault();
    }

    public async Task<AmData<AmArtistAttributes>> GetAppleMusicArtistById(string artistId)
    {
        var result = await this._appleMusicApi.GetArtistAsync(artistId);
        Statistics.AppleMusicApiCalls.Inc();

        return result?.Data?.FirstOrDefault();
    }
}
