using System.Linq;
using System.Threading.Tasks;
using YoutubeSearch;

namespace FMBot.Services
{
    internal class YoutubeService
    {
        private readonly VideoSearch videoSearch = new VideoSearch();

        public VideoInformation GetSearchResult(string searchValue)
        {
            VideoInformation result = videoSearch.SearchQuery(searchValue, 1).ElementAt(0);

            return result;
        }
    }
}
