using System.Linq;
using System.Threading.Tasks;
using FMBot.AppleMusic;
using FMBot.AppleMusic.Models;

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

        return results?.FirstOrDefault();
    }

    public async Task<AmData<AmSongAttributes>> GetAppleMusicSong(string searchQuery)
    {
        var results = await this._appleMusicApi.SearchSongAsync(searchQuery);

        return results?.FirstOrDefault();
    }
}
