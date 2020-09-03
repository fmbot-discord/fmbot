using System.Linq;
using System.Threading.Tasks;
using FMBot.Bot.Configurations;
using Google.Apis.Services;
using Google.Apis.YouTube.v3;
using Google.Apis.YouTube.v3.Data;

namespace FMBot.Bot.Services
{
    public class YoutubeService
    {
        public async Task<SearchResult> GetSearchResult(string searchValue)
        {
            var youtubeService = new YouTubeService(new BaseClientService.Initializer()
            {
                ApiKey = ConfigData.Data.Google.ApiKey,
                ApplicationName = this.GetType().ToString()
            });

            var searchListRequest = youtubeService.Search.List("snippet");
            searchListRequest.Q = searchValue;
            searchListRequest.MaxResults = 1;

            var searchListResponse = await searchListRequest.ExecuteAsync();

            var results = searchListResponse.Items
                .Where(w => w.Kind == "youtube#searchResult")
                .ToList();

            if (results.Any())
            {
                return results.First();
            }

            return null;
        }
    }
}
