using System.Linq;
using System.Threading.Tasks;
using FMBot.Bot.Configurations;
using FMBot.Youtube.Domain.Models;
using FMBot.Youtube.Services;
using Google.Apis.Services;
using Google.Apis.YouTube.v3;
using Google.Apis.YouTube.v3.Data;

namespace FMBot.Bot.Services
{
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
    }
}
