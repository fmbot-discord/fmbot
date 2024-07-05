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

    public async Task<AmData<AmArtistAttributes>> GetAppleMusicArtist(string searchQuery)
    {
        var results = await this._appleMusicApi.SearchArtistAsync(searchQuery);
        Statistics.AppleMusicApiCalls.Inc();

        return results?.FirstOrDefault();
    }

    public async Task<AmData<AmAlbumAttributes>> GetAppleMusicAlbum(string searchQuery)
    {
        var results = await this._appleMusicApi.SearchAlbumAsync(searchQuery);
        Statistics.AppleMusicApiCalls.Inc();

        return results?.FirstOrDefault();
    }

    public async Task<AmData<AmSongAttributes>> GetAppleMusicSong(string searchQuery)
    {
        var results = await this._appleMusicApi.SearchSongAsync(searchQuery);
        Statistics.AppleMusicApiCalls.Inc();

        return results?.FirstOrDefault();
    }
}
