using System.Linq;
using FMBot.YoutubeSearch;

namespace FMBot.Bot.Services
{
    internal class YoutubeService
    {
        private readonly VideoSearch _videoSearch = new VideoSearch();

        public VideoInformation GetSearchResult(string searchValue)
        {
            VideoInformation result = this._videoSearch.SearchQuery(searchValue, 1).ElementAt(0);

            return result;
        }
    }
}
