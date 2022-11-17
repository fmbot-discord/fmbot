using System.Threading.Tasks;
using FMBot.Domain.Models;
using Microsoft.Extensions.Options;
using RestSharp.Authenticators;
using RestSharp;
using System.Web;

namespace FMBot.Bot.Services.ThirdParty;

public class DiscogsService
{
    private readonly BotSettings _botSettings;

    public DiscogsService(IOptions<BotSettings> botSettings)
    {
        this._botSettings = botSettings.Value;
    }

    public async Task<DiscogsAuth> GetDiscogsAuthLink()
    {
        var client = new RestClient($"https://api.discogs.com/")
        {
            Authenticator = OAuth1Authenticator.ForRequestToken(this._botSettings.Discogs.Key, this._botSettings.Discogs.Secret),
        };

        var request = new RestRequest("oauth/request_token");
        request.AddHeader("User-Agent", ".fmbot/1.0 +https://fmbot.xyz/");
        var response = await client.ExecuteAsync(request);

        if (response.IsSuccessStatusCode)
        {
            var query = HttpUtility.ParseQueryString(response.Content);
            var oauthToken = query.Get("oauth_token");
            var oauthTokenSecret = query.Get("oauth_token_secret");

            var loginUrl = $"https://discogs.com/oauth/authorize?oauth_token={oauthToken}";

            return new DiscogsAuth(loginUrl, oauthToken, oauthTokenSecret);
        }

        return null;
    }

    public record DiscogsAuth(string LoginUrl, string OathToken, string OauthTokenSecret);

    public async Task<bool> StoreDiscogsAuth(int userId, DiscogsAuth discogsAuth, string verifier)
    {
        var client = new RestClient($"https://api.discogs.com/")
        {
            Authenticator = OAuth1Authenticator.ForAccessToken(
                this._botSettings.Discogs.Key,
                this._botSettings.Discogs.Secret,
                discogsAuth.OathToken,
                discogsAuth.OauthTokenSecret,
                verifier
            ),
        };

        var request = new RestRequest("oauth/access_token", Method.Post);
        request.AddHeader("User-Agent", ".fmbot/1.0 +https://fmbot.xyz/");
        var response = await client.ExecuteAsync(request);

        if (response.IsSuccessStatusCode)
        {
            var query = HttpUtility.ParseQueryString(response.Content);
            var oauthToken = query.Get("oauth_token");
            var oauthTokenSecret = query.Get("oauth_token_secret");

            // TODO: save creds
        }
        else
        {
            return false;
        }


        return true;
    }
}
