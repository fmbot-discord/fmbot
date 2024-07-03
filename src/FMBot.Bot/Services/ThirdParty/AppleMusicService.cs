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

    public async Task<AmData<AmSongAttributes>> GetAppleMusicSong(string searchQuery)
    {
        var results = await this._appleMusicApi.SearchSongAsync(searchQuery);

        return results?.FirstOrDefault();
    }
}
