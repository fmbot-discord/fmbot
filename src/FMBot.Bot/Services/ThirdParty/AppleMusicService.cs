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

    public AppleMusicService(AppleMusicApi appleMusicApi)
    {
        this._appleMusicApi = appleMusicApi;
    }

    public async Task<AmData<AmArtistAttributes>> GetAppleMusicArtist(string artist)
    {
        var results = await this._appleMusicApi.SearchArtistAsync(artist);
        Statistics.AppleMusicApiCalls.Inc();

        return results?.FirstOrDefault(f => f?.Attributes?.Name != null &&
                                            f.Attributes.Name.Equals(artist, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<AmData<AmAlbumAttributes>> GetAppleMusicAlbum(string artist, string albumName)
    {
        var results = await this._appleMusicApi.SearchAlbumAsync($"{artist} - {albumName}");
        Statistics.AppleMusicApiCalls.Inc();

        return results?.FirstOrDefault(f => f?.Attributes?.Name != null &&
                                            f.Attributes.Name.Contains(albumName, StringComparison.OrdinalIgnoreCase) &&
                                            f.Attributes.ArtistName.Contains(artist, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<AmData<AmSongAttributes>> GetAppleMusicSong(string artist, string songName)
    {
        var results = await this._appleMusicApi.SearchSongAsync($"{artist} - {songName}");
        Statistics.AppleMusicApiCalls.Inc();

        return results?.FirstOrDefault(f => f?.Attributes?.Name != null &&
                                            f.Attributes.Name.Contains(songName, StringComparison.OrdinalIgnoreCase) &&
                                            f.Attributes.ArtistName.Contains(artist, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<AmData<AmSongAttributes>> SearchAppleMusicSong(string searchQuery)
    {
        var results = await this._appleMusicApi.SearchSongAsync(searchQuery);
        Statistics.AppleMusicApiCalls.Inc();

        return results?.FirstOrDefault();
    }
}
