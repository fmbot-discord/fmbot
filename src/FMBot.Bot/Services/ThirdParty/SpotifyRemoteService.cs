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
    public string AlbumName { get; set; }
    public string AlbumUri { get; set; }
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
            AlbumName = track.Album?.Name,
            AlbumUri = track.Album?.Uri,
            AlbumImageUrl = track.Album?.Images?.FirstOrDefault()?.Url
        };
    }
}

public class RemoteAlbum
{
    public string Id { get; set; }
    public string Uri { get; set; }
    public string Name { get; set; }
    public string ArtistName { get; set; }
    public string AlbumImageUrl { get; set; }
    public List<RemoteTrack> Tracks { get; set; } = [];
}

public class RemoteArtist
{
    public string Id { get; set; }
    public string Uri { get; set; }
    public string Name { get; set; }
    public string ImageUrl { get; set; }
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
        var dbTrack = await db.Tracks
            .Include(f => f.Album)
            .FirstOrDefaultAsync(f => f.SpotifyId == spotifyId);

        if (dbTrack != null)
        {
            return new RemoteTrack
            {
                Id = spotifyId,
                Uri = $"spotify:track:{spotifyId}",
                Name = dbTrack.Name,
                ArtistName = dbTrack.ArtistName,
                AlbumName = dbTrack.AlbumName,
                AlbumUri = dbTrack.Album?.SpotifyId != null ? $"spotify:album:{dbTrack.Album.SpotifyId}" : null
            };
        }

        var track = await this._spotifyService.GetTrackById(spotifyId);
        return RemoteTrack.From(track);
    }

    public async Task<RemoteAlbum> ResolveAlbumByIdAsync(string spotifyId)
    {
        var album = await this._spotifyService.GetAlbumById(spotifyId);
        return MapAlbum(album);
    }

    public async Task<RemoteAlbum> ResolveAlbumByNameAsync(string artistName, string albumName)
    {
        var album = await this._spotifyService.GetAlbumFromSpotify(albumName, artistName);
        if (album == null)
        {
            var search = await this._spotifyService.GetSearchResultAsync($"{albumName} {artistName}",
                SearchRequest.Types.Album);
            var match = search.Albums?.Items?.FirstOrDefault();
            if (match != null)
            {
                album = await this._spotifyService.GetAlbumById(match.Id);
            }
        }

        return MapAlbum(album);
    }

    private static RemoteAlbum MapAlbum(FullAlbum album)
    {
        if (album == null)
        {
            return null;
        }

        var imageUrl = album.Images?.FirstOrDefault()?.Url;
        var artistName = album.Artists?.FirstOrDefault()?.Name;

        return new RemoteAlbum
        {
            Id = album.Id,
            Uri = album.Uri,
            Name = album.Name,
            ArtistName = artistName,
            AlbumImageUrl = imageUrl,
            Tracks = album.Tracks?.Items?
                .Select(t => new RemoteTrack
                {
                    Id = t.Id,
                    Uri = t.Uri,
                    Name = t.Name,
                    ArtistName = t.Artists?.FirstOrDefault()?.Name ?? artistName,
                    AlbumName = album.Name,
                    AlbumUri = album.Uri,
                    AlbumImageUrl = imageUrl
                }).ToList() ?? []
        };
    }

    public async Task<RemoteTrack> ResolveArtistTopTrackByIdAsync(string spotifyId)
    {
        var topTracks = await this._spotifyService.GetArtistTopTracks(spotifyId);
        return RemoteTrack.From(topTracks.FirstOrDefault());
    }

    public async Task<RemoteTrack> ResolveArtistTopTrackByNameAsync(string artistName)
    {
        var artist = await ResolveArtistByNameAsync(artistName);
        if (artist == null)
        {
            return null;
        }

        return await ResolveArtistTopTrackByIdAsync(artist.Id);
    }

    public async Task<RemoteArtist> ResolveArtistByIdAsync(string spotifyId)
    {
        var artist = await this._spotifyService.GetArtistById(spotifyId);
        return MapArtist(artist);
    }

    public async Task<RemoteArtist> ResolveArtistByNameAsync(string artistName)
    {
        var artist = await this._spotifyService.GetArtistFromSpotify(artistName);
        if (artist == null)
        {
            var search = await this._spotifyService.GetSearchResultAsync(artistName, SearchRequest.Types.Artist);
            artist = search.Artists?.Items?.FirstOrDefault();
        }

        return MapArtist(artist);
    }

    private static RemoteArtist MapArtist(FullArtist artist)
    {
        if (artist == null)
        {
            return null;
        }

        return new RemoteArtist
        {
            Id = artist.Id,
            Uri = artist.Uri,
            Name = artist.Name,
            ImageUrl = artist.Images?.FirstOrDefault()?.Url
        };
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
            var result = await GetClient(token).Library
                .CheckItems(new LibraryCheckItemsRequest([$"spotify:track:{trackId}"]));
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

    public async Task<RemoteActionResult> QueueAlbumAsync(UserToken token, RemoteAlbum album)
    {
        var result = RemoteActionResult.Ok;
        foreach (var track in album.Tracks)
        {
            result = await QueueAsync(token, track.Uri);
            if (result != RemoteActionResult.Ok)
            {
                return result;
            }
        }

        return result;
    }

    public Task<RemoteActionResult> PlayContextAsync(UserToken token, string contextUri) =>
        Execute(token, c => c.Player.ResumePlayback(new PlayerResumePlaybackRequest { ContextUri = contextUri }));

    public Task<RemoteActionResult> SkipAsync(UserToken token) =>
        Execute(token, c => c.Player.SkipNext());

    public Task<RemoteActionResult> PreviousAsync(UserToken token) =>
        Execute(token, c => c.Player.SkipPrevious());

    public Task<RemoteActionResult> ResumeAsync(UserToken token) =>
        Execute(token, c => c.Player.ResumePlayback());

    public async Task<RemoteActionResult> PlayTrackAsync(UserToken token, string trackUri, string albumUri = null)
    {
        if (string.IsNullOrEmpty(albumUri))
        {
            return await Execute(token,
                c => c.Player.ResumePlayback(new PlayerResumePlaybackRequest { Uris = [trackUri] }));
        }

        var result = await Execute(token, c => c.Player.ResumePlayback(new PlayerResumePlaybackRequest
        {
            ContextUri = albumUri,
            OffsetParam = new PlayerResumePlaybackRequest.Offset { Uri = trackUri }
        }));

        if (result == RemoteActionResult.Error)
        {
            return await Execute(token,
                c => c.Player.ResumePlayback(new PlayerResumePlaybackRequest { Uris = [trackUri] }));
        }

        return result;
    }

    public Task<RemoteActionResult> PauseAsync(UserToken token) =>
        Execute(token, c => c.Player.PausePlayback());

    public Task<RemoteActionResult> TransferAsync(UserToken token, string deviceId) =>
        Execute(token, c => c.Player.TransferPlayback(new PlayerTransferPlaybackRequest([deviceId]) { Play = true }));

    public Task<RemoteActionResult> LikeAsync(UserToken token, string trackId) =>
        ExecuteLibraryWrite(token, trackId,
            (connector, uri, parameters) => connector.Put(uri, parameters, null, default));

    public Task<RemoteActionResult> UnlikeAsync(UserToken token, string trackId) =>
        ExecuteLibraryWrite(token, trackId,
            (connector, uri, parameters) => connector.Delete(uri, parameters, null, default));

    private async Task<RemoteActionResult> ExecuteLibraryWrite(UserToken token, string trackId,
        Func<IAPIConnector, Uri, IDictionary<string, string>, Task> action)
    {
        try
        {
            Statistics.SpotifyApiCalls.Inc();

            var uri = new Uri("me/library", UriKind.Relative);
            var parameters = new Dictionary<string, string> { ["uris"] = $"spotify:track:{trackId}" };
            await action(GetConnector(token), uri, parameters);
            return RemoteActionResult.Ok;
        }
        catch (APIException e)
        {
            var status = e.Response?.StatusCode;
            return status == HttpStatusCode.Unauthorized
                ? RemoteActionResult.NotConnected
                : LogAndError(e, token.DiscordUserId, status);
        }
        catch (Exception e)
        {
            Log.Error(e, "SpotifyRemote: Library action failed for {discordUserId}", token.DiscordUserId);
            return RemoteActionResult.Error;
        }
    }

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
        return new SpotifyClient(GetConfig(token));
    }

    private IAPIConnector GetConnector(UserToken token)
    {
        var config = GetConfig(token);
        return new APIConnector(config.BaseAddress, config.Authenticator, config.JSONSerializer,
            config.HTTPClient, config.RetryHandler, config.HTTPLogger);
    }

    private SpotifyClientConfig GetConfig(UserToken token)
    {
        return SpotifyClientConfig
            .CreateDefault()
            .WithHTTPClient(new NetHttpClient(this._httpClient))
            .WithAuthenticator(new TokenAuthenticator(token.AccessToken, "Bearer"));
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
