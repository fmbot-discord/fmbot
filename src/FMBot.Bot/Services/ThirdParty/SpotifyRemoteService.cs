using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using FMBot.Domain;
using FMBot.Domain.Models;
using FMBot.Persistence.EntityFrameWork;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Serilog;
using Shared.Domain.Enums;
using Shared.Domain.Models;
using SpotifyAPI.Web;
using SpotifyAPI.Web.Http;

namespace FMBot.Bot.Services.ThirdParty;

public enum RemoteActionResult
{
    Ok,
    NotConnected,
    PremiumRequired,
    NoActiveDevice,
    NotFound,
    Restriction,
    Error
}

public class RemoteTrack
{
    public string Id { get; set; }
    public string Uri { get; set; }
    public string Name { get; set; }
    public string ArtistName { get; set; }
    public string AlbumImageUrl { get; set; }

    public static RemoteTrack From(FullTrack track)
    {
        if (track == null)
        {
            return null;
        }

        return new RemoteTrack
        {
            Id = track.Id,
            Uri = track.Uri,
            Name = track.Name,
            ArtistName = track.Artists?.FirstOrDefault()?.Name,
            AlbumImageUrl = track.Album?.Images?.FirstOrDefault()?.Url
        };
    }
}

public class SpotifyRemoteService
{
    private readonly IDbContextFactory<FMBotDbContext> _contextFactory;
    private readonly BotSettings _botSettings;
    private readonly HttpClient _httpClient;
    private readonly SpotifyService _spotifyService;

    private const string Scopes =
        "user-modify-playback-state user-read-playback-state user-read-currently-playing user-read-recently-played user-library-modify user-library-read";

    public SpotifyRemoteService(IDbContextFactory<FMBotDbContext> contextFactory,
        IOptions<BotSettings> botSettings,
        HttpClient httpClient,
        SpotifyService spotifyService)
    {
        this._contextFactory = contextFactory;
        this._botSettings = botSettings.Value;
        this._httpClient = httpClient;
        this._spotifyService = spotifyService;
    }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(this._botSettings.Spotify?.Key) &&
        !string.IsNullOrWhiteSpace(this._botSettings.Spotify?.RedirectUri) &&
        !string.IsNullOrWhiteSpace(this._botSettings.Spotify?.StateSecret);

    public async Task<UserToken> GetActiveTokenAsync(ulong discordUserId)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        var token = await db.UserTokens.FirstOrDefaultAsync(f =>
            f.DiscordUserId == discordUserId && f.Service == TokenService.Spotify);

        if (token == null)
        {
            return null;
        }

        if (DateTime.UtcNow >= token.TokenExpiresAt.AddSeconds(-60))
        {
            var refreshed = await RefreshToken(token, db);
            if (!refreshed)
            {
                return null;
            }
        }

        return token;
    }

    public async Task RemoveTokenAsync(ulong discordUserId)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        var token = await db.UserTokens.FirstOrDefaultAsync(f =>
            f.DiscordUserId == discordUserId && f.Service == TokenService.Spotify);

        if (token != null)
        {
            db.UserTokens.Remove(token);
            await db.SaveChangesAsync();
        }
    }

    public string BuildAuthUrl(ulong discordUserId)
    {
        var nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(8));
        var state = SignState($"{discordUserId}:{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}:{nonce}");

        return "https://accounts.spotify.com/authorize" +
               $"?client_id={this._botSettings.Spotify.Key}" +
               "&response_type=code" +
               $"&redirect_uri={Uri.EscapeDataString(this._botSettings.Spotify.RedirectUri)}" +
               $"&scope={Uri.EscapeDataString(Scopes)}" +
               $"&state={Uri.EscapeDataString(state)}";
    }

    public async Task<bool> WaitForConnectionAsync(ulong discordUserId)
    {
        for (var i = 0; i < 20; i++)
        {
            await Task.Delay(6000);

            await using var db = await this._contextFactory.CreateDbContextAsync();
            var connected = await db.UserTokens.AnyAsync(f =>
                f.DiscordUserId == discordUserId && f.Service == TokenService.Spotify);

            if (connected)
            {
                return true;
            }
        }

        return false;
    }

    public async Task<RemoteTrack> ResolveSpotifyTrackAsync(string artistName, string trackName)
    {
        var track = await this._spotifyService.GetTrackFromSpotify(trackName, artistName);
        if (track == null)
        {
            var search = await this._spotifyService.GetSearchResultAsync($"{trackName} {artistName}");
            track = search.Tracks?.Items?.FirstOrDefault();
        }

        return RemoteTrack.From(track);
    }

    public async Task<RemoteTrack> ResolveTrackByIdAsync(string spotifyId)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        var dbTrack = await db.Tracks.FirstOrDefaultAsync(f => f.SpotifyId == spotifyId);

        if (dbTrack != null)
        {
            return new RemoteTrack
            {
                Id = spotifyId,
                Uri = $"spotify:track:{spotifyId}",
                Name = dbTrack.Name,
                ArtistName = dbTrack.ArtistName
            };
        }

        var track = await this._spotifyService.GetTrackById(spotifyId);
        return RemoteTrack.From(track);
    }

    public async Task<CurrentlyPlayingContext> GetPlaybackAsync(UserToken token)
    {
        try
        {
            Statistics.SpotifyApiCalls.Inc();
            return await GetClient(token).Player.GetCurrentPlayback();
        }
        catch (Exception e)
        {
            Log.Warning(e, "SpotifyRemote: Failed to get playback for {discordUserId}", token.DiscordUserId);
            return null;
        }
    }

    public async Task<List<RemoteTrack>> GetQueueAsync(UserToken token, int limit = 5)
    {
        try
        {
            Statistics.SpotifyApiCalls.Inc();
            var queue = await GetClient(token).Player.GetQueue();

            return queue.Queue
                .OfType<FullTrack>()
                .Take(limit)
                .Select(RemoteTrack.From)
                .ToList();
        }
        catch (Exception e)
        {
            Log.Warning(e, "SpotifyRemote: Failed to get queue for {discordUserId}", token.DiscordUserId);
            return [];
        }
    }

    public async Task<List<Device>> GetDevicesAsync(UserToken token)
    {
        try
        {
            Statistics.SpotifyApiCalls.Inc();
            var devices = await GetClient(token).Player.GetAvailableDevices();
            return devices?.Devices ?? [];
        }
        catch (Exception e)
        {
            Log.Warning(e, "SpotifyRemote: Failed to get devices for {discordUserId}", token.DiscordUserId);
            return [];
        }
    }

    public async Task<bool> IsLikedAsync(UserToken token, string trackId)
    {
        try
        {
            Statistics.SpotifyApiCalls.Inc();
            var result = await GetClient(token).Library.CheckItems(new LibraryCheckItemsRequest([$"spotify:track:{trackId}"]));
            return result.FirstOrDefault();
        }
        catch (Exception e)
        {
            Log.Warning(e, "SpotifyRemote: Failed to check library for {discordUserId}", token.DiscordUserId);
            return false;
        }
    }

    public Task<RemoteActionResult> QueueAsync(UserToken token, string trackUri) =>
        Execute(token, c => c.Player.AddToQueue(new PlayerAddToQueueRequest(trackUri)));

    public Task<RemoteActionResult> SkipAsync(UserToken token) =>
        Execute(token, c => c.Player.SkipNext());

    public Task<RemoteActionResult> PreviousAsync(UserToken token) =>
        Execute(token, c => c.Player.SkipPrevious());

    public Task<RemoteActionResult> ResumeAsync(UserToken token) =>
        Execute(token, c => c.Player.ResumePlayback());

    public Task<RemoteActionResult> PauseAsync(UserToken token) =>
        Execute(token, c => c.Player.PausePlayback());

    public Task<RemoteActionResult> TransferAsync(UserToken token, string deviceId) =>
        Execute(token, c => c.Player.TransferPlayback(new PlayerTransferPlaybackRequest([deviceId]) { Play = true }));

    public Task<RemoteActionResult> LikeAsync(UserToken token, string trackId) =>
        Execute(token, c => c.Library.SaveItems(new LibrarySaveItemsRequest([$"spotify:track:{trackId}"])));

    public Task<RemoteActionResult> UnlikeAsync(UserToken token, string trackId) =>
        Execute(token, c => c.Library.RemoveItems(new LibraryRemoveItemsRequest([$"spotify:track:{trackId}"])));

    private async Task<RemoteActionResult> Execute(UserToken token, Func<SpotifyClient, Task> action)
    {
        try
        {
            Statistics.SpotifyApiCalls.Inc();
            await action(GetClient(token));
            return RemoteActionResult.Ok;
        }
        catch (APIException e)
        {
            var status = e.Response?.StatusCode;
            return status switch
            {
                HttpStatusCode.NotFound => RemoteActionResult.NoActiveDevice,
                HttpStatusCode.Forbidden when IsPremiumRequired(e) => RemoteActionResult.PremiumRequired,
                HttpStatusCode.Forbidden => RemoteActionResult.Restriction,
                _ => LogAndError(e, token.DiscordUserId, status)
            };
        }
        catch (Exception e)
        {
            Log.Error(e, "SpotifyRemote: Action failed for {discordUserId}", token.DiscordUserId);
            return RemoteActionResult.Error;
        }
    }

    private static bool IsPremiumRequired(APIException e) =>
        e.Message?.Contains("premium", StringComparison.OrdinalIgnoreCase) == true;

    private static RemoteActionResult LogAndError(APIException e, ulong discordUserId, HttpStatusCode? status)
    {
        Log.Warning(e, "SpotifyRemote: Spotify API error {status} for {discordUserId}", status, discordUserId);
        return RemoteActionResult.Error;
    }

    private SpotifyClient GetClient(UserToken token)
    {
        var config = SpotifyClientConfig
            .CreateDefault()
            .WithHTTPClient(new NetHttpClient(this._httpClient))
            .WithAuthenticator(new TokenAuthenticator(token.AccessToken, "Bearer"));

        return new SpotifyClient(config);
    }

    private async Task<bool> RefreshToken(UserToken token, FMBotDbContext db)
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "https://accounts.spotify.com/api/token")
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    { "grant_type", "refresh_token" },
                    { "refresh_token", token.RefreshToken }
                })
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
                Convert.ToBase64String(
                    Encoding.UTF8.GetBytes($"{this._botSettings.Spotify.Key}:{this._botSettings.Spotify.Secret}")));

            var response = await this._httpClient.SendAsync(request);

            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadFromJsonAsync<SpotifyTokenResponse>();

                token.AccessToken = json.AccessToken;
                if (!string.IsNullOrEmpty(json.RefreshToken))
                {
                    token.RefreshToken = json.RefreshToken;
                }

                token.TokenExpiresAt = DateTime.UtcNow.AddSeconds(json.ExpiresIn);
                token.LastUpdated = DateTime.UtcNow;

                db.Update(token);
                await db.SaveChangesAsync();
                return true;
            }

            var errorContent = await response.Content.ReadAsStringAsync();
            if (errorContent.Contains("invalid_grant", StringComparison.OrdinalIgnoreCase))
            {
                db.UserTokens.Remove(token);
                await db.SaveChangesAsync();
                Log.Information("SpotifyRemote: Removed invalid Spotify token for {discordUserId}",
                    token.DiscordUserId);
                return false;
            }

            Log.Error("SpotifyRemote: Failed to refresh Spotify token for {discordUserId}: {errorContent}",
                token.DiscordUserId, errorContent);
            return false;
        }
        catch (Exception e)
        {
            Log.Error(e, "SpotifyRemote: Exception while refreshing Spotify token for {discordUserId}",
                token.DiscordUserId);
            return false;
        }
    }

    private string SignState(string payload)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(this._botSettings.Spotify.StateSecret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        var signature = Convert.ToBase64String(hash).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return $"{payload}.{signature}";
    }

    private class SpotifyTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; }

        [JsonPropertyName("refresh_token")]
        public string RefreshToken { get; set; }

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }

        [JsonPropertyName("scope")]
        public string Scope { get; set; }
    }
}
