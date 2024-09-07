using System;
using System.Linq;
using System.Threading.Tasks;
using FMBot.AppleMusic;
using FMBot.AppleMusic.Models;
using FMBot.Domain;

namespace FMBot.Bot.Services.ThirdParty;

public class AppleMusicService
{
    private readonly AppleMusicApi _appleMusicApi;
    private readonly AppleMusicAltApi _appleMusicAltApi;

    public AppleMusicService(AppleMusicApi appleMusicApi, AppleMusicAltApi appleMusicAltApi)
    {
        this._appleMusicApi = appleMusicApi;
        this._appleMusicAltApi = appleMusicAltApi;
    }

    public async Task<AmData<AmArtistAttributes>> GetAppleMusicArtist(string artist)
    {
        var results = await this._appleMusicApi.SearchArtistAsync(artist);
        Statistics.AppleMusicApiCalls.Inc();

        return results?.FirstOrDefault(f => f?.Attributes?.Name != null &&
                                            f.Attributes.Name.Equals(artist, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<AmData<AmAlbumAttributes>> GetAppleMusicAlbum(string artist, string albumName,
        bool getMotionCovers = false)
    {
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
}
