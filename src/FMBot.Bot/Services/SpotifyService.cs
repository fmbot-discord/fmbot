using System.Linq;
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
            var auth = new CredentialsAuth(ConfigData.Data.Spotify.Key, ConfigData.Data.Spotify.Secret);

            var token = await auth.GetToken();

            var spotify = new SpotifyWebAPI
            {
                TokenType = token.TokenType,
                AccessToken = token.AccessToken,
                UseAuth = true
            };

            return await spotify.SearchItemsAsync(searchValue, searchType);
        }

        public async Task<string> GetArtistImageAsync(string artistName)
        {
            //Create the auth object
            var auth = new CredentialsAuth(ConfigData.Data.Spotify.Key, ConfigData.Data.Spotify.Secret);

            var token = await auth.GetToken();

            var spotify = new SpotifyWebAPI
            {
                TokenType = token.TokenType,
                AccessToken = token.AccessToken,
                UseAuth = true
            };

            var results = await spotify.SearchItemsAsync(artistName, SearchType.Artist);

            if (results.Artists?.Items?.Any() == true)
            {
                var spotifyArtist = results.Artists.Items.FirstOrDefault();
                if (spotifyArtist.Images.Any() && spotifyArtist.Name.ToLower() == artistName.ToLower())
                {
                    return spotifyArtist.Images.OrderByDescending(o => o.Height).FirstOrDefault().Url;
                }
            }

            return null;
        }
    }
}
