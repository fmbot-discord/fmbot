using RestSharp.Authenticators;
using RestSharp;
using System.Web;
using FMBot.Discogs.Models;
using FMBot.Domain;
using Microsoft.Extensions.Configuration;
using RestSharp.Serializers.Json;

namespace FMBot.Discogs.Apis;

public class DiscogsApi
{
    private readonly HttpClient _client;

    private const string UserAgent = ".fmbot/1.0 +https://fmbot.xyz/";
    private readonly string _key;
    private readonly string _secret;

    public DiscogsApi(IConfiguration configuration, HttpClient httpClient)
    {
        this._key = configuration.GetSection("Discogs:Key").Value;
        this._secret = configuration.GetSection("Discogs:Secret").Value;
        this._client = httpClient;
        this._client.BaseAddress = new Uri("https://api.discogs.com/");
    }

    public async Task<DiscogsAuthInitialization> GetDiscogsAuthLink()
    {
        var client = new RestClient(this._client)
        {
            Authenticator = OAuth1Authenticator.ForRequestToken(this._key, this._secret),
        };

        var request = new RestRequest("oauth/request_token");
        request.AddHeader("User-Agent", UserAgent);
        var response = await client.ExecuteAsync(request);
        Statistics.DiscogsApiCalls.Inc();

        if (response.IsSuccessful)
        {
            var query = HttpUtility.ParseQueryString(response.Content);
            var oauthToken = query.Get("oauth_token");
            var oauthTokenSecret = query.Get("oauth_token_secret");

            var loginUrl = $"https://discogs.com/oauth/authorize?oauth_token={oauthToken}";

            return new DiscogsAuthInitialization(loginUrl, oauthToken, oauthTokenSecret);
        }

        return null;
    }

    public async Task<DiscogsAuth> StoreDiscogsAuth(DiscogsAuthInitialization discogsAuth, string verifier)
    {
        var client = new RestClient(this._client)
        {
            Authenticator = OAuth1Authenticator.ForAccessToken(
                this._key,
                this._secret,
                discogsAuth.OathToken,
                discogsAuth.OauthTokenSecret,
                verifier
            )
        };

        var request = new RestRequest("oauth/access_token", Method.Post);
        request.AddHeader("User-Agent", UserAgent);
        var response = await client.ExecuteAsync(request);
        Statistics.DiscogsApiCalls.Inc();

        if (response.IsSuccessful)
        {
            var query = HttpUtility.ParseQueryString(response.Content);
            var oauthToken = query.Get("oauth_token");
            var oauthTokenSecret = query.Get("oauth_token_secret");

            return new DiscogsAuth(oauthToken, oauthTokenSecret);
        }

        return null;
    }

    private RestClient GetClient(DiscogsAuth discogsAuth)
    {
        var client = new RestClient(this._client)
        {
            Authenticator = OAuth1Authenticator.ForAccessToken(
                this._key,
                this._secret,
                discogsAuth.AccessToken,
                discogsAuth.AccessTokenSecret
            )
        };

        client.UseSystemTextJson();
        client.AddDefaultHeader("User-Agent", UserAgent);

        return client;
    }

    public async Task<DiscogsIdentity> GetIdentity(DiscogsAuth discogsAuth)
    {
        var client = GetClient(discogsAuth);

        var request = new RestRequest("oauth/identity");
        var response = await client.ExecuteAsync<DiscogsIdentity>(request);
        Statistics.DiscogsApiCalls.Inc();

        return response.Data;
    }

    public async Task<DiscogsUserReleases> GetUserReleases(DiscogsAuth discogsAuth, string discogsUser, int pages = 1)
    {
        var client = GetClient(discogsAuth);
        var request = new RestRequest($"users/{discogsUser}/collection/folders/0/releases");

        request.AddParameter("per_page", 100);
        request.AddParameter("sort", "added");
        request.AddParameter("sort_order", "desc");

        var response = await client.ExecuteAsync<DiscogsUserReleases>(request);
        Statistics.DiscogsApiCalls.Inc();

        if (response.IsSuccessStatusCode &&
            response.Data != null &&
            response.Data.Releases.Count == 100 &&
            pages > 1)
        {
            for (var i = 2; i <= pages; i++)
            {
                request.Parameters.RemoveParameter("page");
                request.AddParameter("page", i);

                var pageResponse = await client.ExecuteAsync<DiscogsUserReleases>(request);
                Statistics.DiscogsApiCalls.Inc();

                if (pageResponse.Data?.Releases != null && pageResponse.Data.Releases.Any())
                {
                    response.Data.Releases.AddRange(pageResponse.Data.Releases);

                    if (pageResponse.Data.Releases.Count < 100)
                    {
                        break;
                    }
                }
                else
                {
                    break;
                }
            }
        }

        return response.Data;
    }

    public async Task<DiscogsCollectionValue> GetCollectionValue(DiscogsAuth discogsAuth, string discogsUser)
    {
        var client = GetClient(discogsAuth);
        var request = new RestRequest($"users/{discogsUser}/collection/value");

        var response = await client.ExecuteAsync<DiscogsCollectionValue>(request);
        Statistics.DiscogsApiCalls.Inc();

        return response.Data;
    }

    public async Task<DiscogsFullRelease> GetRelease(DiscogsAuth discogsAuth, int releaseId)
    {
        var client = GetClient(discogsAuth);
        var request = new RestRequest($"releases/{releaseId}");

        var response = await client.ExecuteAsync<DiscogsFullRelease>(request);
        Statistics.DiscogsApiCalls.Inc();

        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return response.Data;
    }
}
