using SpotifyAPI.Web;
using SpotifyAPI.Web.Auth;
using SpotifyAPI.Web.Enums;
using SpotifyAPI.Web.Models;
using System.Threading.Tasks;
using FMBot.Bot.Configurations;
using static FMBot.Bot.FMBotUtil;

namespace FMBot.Services
{
    internal class SpotifyService
    {
        public async Task<SearchItem> GetSearchResultAsync(string searchValue, SearchType searchType = SearchType.Track)
        {
            //Create the auth object
            CredentialsAuth auth = new CredentialsAuth(ConfigData.Data.SpotifyKey, ConfigData.Data.SpotifySecret);
            
            Token token = await auth.GetToken();

            SpotifyWebAPI spotify = new SpotifyWebAPI()
            {
                TokenType = token.TokenType,
                AccessToken = token.AccessToken,
                UseAuth = true
            };

            SearchItem result = await spotify.SearchItemsAsync(searchValue, SearchType.Track);

            return result;
        }
    }
}
