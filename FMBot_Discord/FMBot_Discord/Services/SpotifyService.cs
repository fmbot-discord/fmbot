using SpotifyAPI.Web;
using SpotifyAPI.Web.Auth;
using SpotifyAPI.Web.Enums;
using SpotifyAPI.Web.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static FMBot.Bot.FMBotUtil;

namespace FMBot.Services
{
    class SpotifyService
    {
        public static JsonCfg.ConfigJson cfgjson = JsonCfg.GetJSONData();

        //Create the auth object
        private static ClientCredentialsAuth auth = new ClientCredentialsAuth()
        {
            ClientId = cfgjson.SpotifyKey,
            ClientSecret = cfgjson.SpotifySecret,
            Scope = Scope.None,
        };
        private static Token token = auth.DoAuth();

        public SpotifyWebAPI spotify = new SpotifyWebAPI()
        {
            TokenType = token.TokenType,
            AccessToken = token.AccessToken,
            UseAuth = true
        };

        public async Task<SearchItem> GetSearchResultAsync (string searchValue, SearchType searchType = SearchType.Track)
        {
            SearchItem result = await spotify.SearchItemsAsync(searchValue, SearchType.Track);

            return result;
        }
    }
}
