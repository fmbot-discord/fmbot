using System.Threading.Tasks;
using FMBot.Youtube.Models;
using FMBot.Youtube.Services;

namespace FMBot.Bot.Services.ThirdParty;

public class YoutubeService
{

    private readonly InvidiousApi _invidiousApi;

    public YoutubeService(InvidiousApi invidiousApi)
    {
        this._invidiousApi = invidiousApi;
    }

    public async Task<InvidiousSearchResult> GetSearchResult(string searchValue)
    {
        return await this._invidiousApi.SearchVideoAsync(searchValue);
    }

    public async Task<InvidiousVideoResult> GetVideoResult(string videoId)
    {
        return await this._invidiousApi.GetVideoAsync(videoId);
    }
}
