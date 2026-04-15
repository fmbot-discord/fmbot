using System.Net;
using RestSharp.Authenticators;
using RestSharp;
using System.Web;
using FMBot.Discogs.Models;
using FMBot.Domain;
using Microsoft.Extensions.Configuration;
using Serilog;

namespace FMBot.Discogs.Apis;

public class DiscogsApi
{
    private readonly HttpClient _client;

    private const string UserAgent = ".fmbot/1.0 +https://fm.bot/";
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
        var options = new RestClientOptions() {
            Authenticator = OAuth1Authenticator.ForRequestToken(
                this._key,
                this._secret
            ),
        };

        var client = new RestClient(this._client, options);

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

        Log.Error("Discogs: Failed to get auth link - Status: {statusCode} | Content: {content}", response.StatusCode, response.Content);
        return null;
    }

    public async Task<DiscogsAuth> StoreDiscogsAuth(DiscogsAuthInitialization discogsAuth, string verifier)
    {
        var options = new RestClientOptions() {
            Authenticator = OAuth1Authenticator.ForAccessToken(
                this._key,
                this._secret,
                discogsAuth.OathToken,
                discogsAuth.OauthTokenSecret,
                verifier
            )
        };

        var client = new RestClient(this._client, options);

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

        Log.Error("Discogs: Failed to store auth - Status: {statusCode} | Content: {content}", response.StatusCode, response.Content);
        return null;
    }

    private RestClient GetClient(DiscogsAuth discogsAuth)
    {
        var options = new RestClientOptions() {
            Authenticator = OAuth1Authenticator.ForAccessToken(
                this._key,
                this._secret,
                discogsAuth.AccessToken,
                discogsAuth.AccessTokenSecret
            ),
        };

        var client = new RestClient(this._client, options);

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

        if (!response.IsSuccessStatusCode)
        {
            if (response.Content != null && response.Content.Contains("User does not exist"))
            {
                Log.Warning("Discogs: User {discogsUser} does not exist or has been deleted", discogsUser);
                throw new HttpRequestException($"Discogs user '{discogsUser}' does not exist or has been deleted.", null, HttpStatusCode.NotFound);
            }

            Log.Error("Discogs: Failed to get releases for {discogsUser} - Status: {statusCode} | Content: {content}",
                discogsUser, response.StatusCode, response.Content);
            return response.Data;
        }

        if (response.Data != null &&
            response.Data.Releases.Count == 100 &&
            pages > 1)
        {
            for (var i = 2; i <= pages; i++)
            {
                await Task.Delay(1000);

                request.Parameters.RemoveParameter("page");
                request.AddParameter("page", i);

                var pageResponse = await client.ExecuteAsync<DiscogsUserReleases>(request);
                Statistics.DiscogsApiCalls.Inc();

                if (!pageResponse.IsSuccessStatusCode)
                {
                    Log.Warning("Discogs: Failed to get page {page} for {discogsUser} - Status: {statusCode} | Content: {content}",
                        i, discogsUser, pageResponse.StatusCode, pageResponse.Content);
                    break;
                }

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

        if (!response.IsSuccessStatusCode)
        {
            if (response.Content != null && response.Content.Contains("User does not exist"))
            {
                Log.Warning("Discogs: User {discogsUser} does not exist or has been deleted", discogsUser);
                throw new HttpRequestException($"Discogs user '{discogsUser}' does not exist or has been deleted.", null, HttpStatusCode.NotFound);
            }

            Log.Error("Discogs: Failed to get collection value for {discogsUser} - Status: {statusCode} | Content: {content}", discogsUser, response.StatusCode, response.Content);
        }

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
            Log.Error("Discogs: Failed to get release {releaseId} - Status: {statusCode} | Content: {content}", releaseId, response.StatusCode, response.Content);
            return null;
        }

        return response.Data;
    }
}
