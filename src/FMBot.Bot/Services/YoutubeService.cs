using System.Linq;
using FMBot.YoutubeSearch;

namespace FMBot.Bot.Services
{
    internal class YoutubeService
    {
        private readonly VideoSearch _videoSearch = new VideoSearch();

        public VideoInformation GetSearchResult(string searchValue)
        {
            var result = this._videoSearch.SearchQuery(searchValue, 1);

            return result.ElementAt(0);
        }
    }
}
