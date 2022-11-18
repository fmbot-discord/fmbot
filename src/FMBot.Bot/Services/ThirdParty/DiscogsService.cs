using System.Threading.Tasks;
using FMBot.Domain.Models;
using Microsoft.Extensions.Options;
using RestSharp.Authenticators;
using RestSharp;
using System.Web;
using DiscogsClient.Data.Result;
using RestSharpHelper.OAuth1;

namespace FMBot.Bot.Services.ThirdParty;

public class DiscogsService
{
    private readonly BotSettings _botSettings;

    private const string UserAgent = ".fmbot/1.0 +https://fmbot.xyz/";

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
        request.AddHeader("User-Agent", UserAgent);
        var response = await client.ExecuteAsync(request);

        if (response.IsSuccessful)
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

    public async Task<DiscogsIdentity> StoreDiscogsAuth(int userId, DiscogsAuth discogsAuth, string verifier)
    {
        var client = new RestClient($"https://api.discogs.com/")
        {
            Authenticator = OAuth1Authenticator.ForAccessToken(
                this._botSettings.Discogs.Key,
                this._botSettings.Discogs.Secret,
                discogsAuth.OathToken,
                discogsAuth.OauthTokenSecret,
                verifier
            )
        };

        var request = new RestRequest("oauth/access_token", Method.POST);
        request.AddHeader("User-Agent", UserAgent);
        var response = await client.ExecuteAsync(request);

        if (response.IsSuccessful)
        {
            var query = HttpUtility.ParseQueryString(response.Content);
            var oauthToken = query.Get("oauth_token");
            var oauthTokenSecret = query.Get("oauth_token_secret");

            var oAuthCompleteInformation = new OAuthCompleteInformation(this._botSettings.Discogs.Key,
                this._botSettings.Discogs.Secret, oauthToken, oauthTokenSecret);
            var discogsClient = new DiscogsClient.DiscogsClient(oAuthCompleteInformation);

            var user = await discogsClient.GetUserIdentityAsync();

            return user;

            // TODO: save creds
        }

        return null;
    }

    public async Task GetCollection()
    {
        



    }
}
