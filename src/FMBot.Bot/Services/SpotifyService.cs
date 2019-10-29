using System.Threading.Tasks;
using FMBot.Bot.Configurations;
using SpotifyAPI.Web;
using SpotifyAPI.Web.Auth;
using SpotifyAPI.Web.Enums;
using SpotifyAPI.Web.Models;

namespace FMBot.Bot.Services
{
    internal class SpotifyService
    {
        public async Task<SearchItem> GetSearchResultAsync(string searchValue, SearchType searchType = SearchType.Track)
        {
            //Create the auth object
            var auth = new CredentialsAuth(ConfigData.Data.SpotifyKey, ConfigData.Data.SpotifySecret);

            var token = await auth.GetToken();

            var spotify = new SpotifyWebAPI
            {
                TokenType = token.TokenType,
                AccessToken = token.AccessToken,
                UseAuth = true
            };

            return await spotify.SearchItemsAsync(searchValue, searchType);
        }
    }
}
