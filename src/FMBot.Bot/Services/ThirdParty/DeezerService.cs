using System;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using FMBot.Bot.Extensions;
using FMBot.Domain;
using Serilog;

namespace FMBot.Bot.Services.ThirdParty;

public sealed record DeezerLookup<T>(T Item, bool Failed) where T : class
{
    public static DeezerLookup<T> Found(T item) => new(item, false);
    public static readonly DeezerLookup<T> NotFound = new(null, false);
    public static readonly DeezerLookup<T> Unavailable = new(null, true);
}

public class DeezerService
{
    private const int NoDataErrorCode = 800;
    private const int QuotaErrorCode = 4;
    private static readonly TimeSpan QuotaPause = TimeSpan.FromSeconds(5);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    private static long _pausedUntilTicks;

    private readonly HttpClient _httpClient;

    public DeezerService(HttpClient httpClient)
    {
        this._httpClient = httpClient;
    }

    public async Task<DeezerLookup<DeezerTrack>> GetTrack(string isrc, string artistName, string trackName)
    {
        if (!string.IsNullOrWhiteSpace(isrc))
        {
            var byIsrc = await Get<DeezerTrack>($"track/isrc:{Uri.EscapeDataString(isrc.Trim())}");
            if (byIsrc.Failed || (byIsrc.Item != null && (ArtistMatches(byIsrc.Item, artistName) || TitleMatches(byIsrc.Item, trackName))))
            {
                return byIsrc;
            }
        }

        var query = $"artist:{Truncate(artistName)} track:\"{Truncate(trackName).Replace("\"", "")}\"";
        var search = await Get<DeezerList<DeezerTrack>>($"search?limit=10&q={Uri.EscapeDataString(query)}");
        if (search.Failed)
        {
            return DeezerLookup<DeezerTrack>.Unavailable;
        }

        var match = search.Item?.Data?
            .OrderByDescending(o => o.Rank ?? 0)
            .FirstOrDefault(t => TrackMatches(t, artistName, trackName));

        return match == null ? DeezerLookup<DeezerTrack>.NotFound : await GetTrackById(match.Id);
    }

    public Task<DeezerLookup<DeezerTrack>> GetTrackById(long deezerId)
    {
        return Get<DeezerTrack>($"track/{deezerId}");
    }

    public async Task<string> GetTrackPreviewUrl(long deezerId)
    {
        var track = await GetTrackById(deezerId);
        return string.IsNullOrEmpty(track.Item?.Preview) ? null : track.Item.Preview;
    }

    public async Task<DeezerLookup<DeezerAlbum>> GetAlbum(string upc, string artistName, string albumName)
    {
        var normalizedUpc = NormalizeUpc(upc);
        if (normalizedUpc != null)
        {
            var byUpc = await Get<DeezerAlbum>($"album/upc:{normalizedUpc}");
            if (byUpc.Item != null || byUpc.Failed)
            {
                return byUpc;
            }
        }

        var query = $"{Truncate(artistName)} {Truncate(albumName)}";
        var search = await Get<DeezerList<DeezerAlbum>>($"search/album?limit=10&q={Uri.EscapeDataString(query)}");
        if (search.Failed)
        {
            return DeezerLookup<DeezerAlbum>.Unavailable;
        }

        var match = search.Item?.Data?
            .FirstOrDefault(a => Eq(a.Title, albumName) && Eq(a.Artist?.Name, artistName));

        return match == null ? DeezerLookup<DeezerAlbum>.NotFound : await GetAlbumById(match.Id);
    }

    public Task<DeezerLookup<DeezerAlbum>> GetAlbumById(long deezerId)
    {
        return Get<DeezerAlbum>($"album/{deezerId}");
    }

    public async Task<DeezerLookup<DeezerArtist>> GetArtist(string artistName)
    {
        var search = await Get<DeezerList<DeezerArtist>>($"search/artist?limit=10&q={Uri.EscapeDataString(Truncate(artistName))}");
        if (search.Failed)
        {
            return DeezerLookup<DeezerArtist>.Unavailable;
        }

        var match = search.Item?.Data?
            .OrderByDescending(o => o.NbFan ?? 0)
            .FirstOrDefault(a => Eq(a.Name, artistName));

        return match == null ? DeezerLookup<DeezerArtist>.NotFound : DeezerLookup<DeezerArtist>.Found(match);
    }

    public Task<DeezerLookup<DeezerArtist>> GetArtistById(long deezerId)
    {
        return Get<DeezerArtist>($"artist/{deezerId}");
    }

    public static bool? ExplicitFromContentCode(int? explicitContentLyrics)
    {
        return explicitContentLyrics switch
        {
            0 or 3 => false,
            1 or 4 => true,
            _ => null
        };
    }

    private static string NormalizeUpc(string upc)
    {
        if (string.IsNullOrWhiteSpace(upc))
        {
            return null;
        }

        var digits = upc.Trim();
        if (!digits.All(char.IsAsciiDigit))
        {
            return null;
        }

        return digits.Length > 12 ? digits.TrimStart('0').PadLeft(12, '0') : digits;
    }

    public static bool IsPlaceholderImage(string imageUrl)
    {
        return string.IsNullOrEmpty(imageUrl) || imageUrl.Contains("/artist//") || imageUrl.Contains("/cover//");
    }

    public static bool IsValidReleaseDate(string releaseDate)
    {
        return !string.IsNullOrEmpty(releaseDate) && !releaseDate.StartsWith("0000");
    }

    private static bool TrackMatches(DeezerTrack track, string artistName, string trackName)
    {
        return TitleMatches(track, trackName) && ArtistMatches(track, artistName);
    }

    private static bool TitleMatches(DeezerTrack track, string trackName)
    {
        return TitleEq(track.Title, trackName) || TitleEq(track.TitleShort, trackName);
    }

    private static bool ArtistMatches(DeezerTrack track, string artistName)
    {
        return Eq(track.Artist?.Name, artistName) ||
               track.Contributors?.Any(c => Eq(c.Name, artistName)) == true;
    }

    private static bool TitleEq(string a, string b)
    {
        if (a == null || b == null)
        {
            return false;
        }

        var left = StringExtensions.SanitizeTrackNameForComparison(a);
        var right = StringExtensions.SanitizeTrackNameForComparison(b);
        return left.Length > 0 && left == right;
    }

    private static bool Eq(string a, string b)
    {
        return a != null && b != null && string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static string Truncate(string value)
    {
        return value.Length > 100 ? value[..100] : value;
    }

    private async Task<DeezerLookup<T>> Get<T>(string path) where T : class
    {
        if (DateTime.UtcNow.Ticks < Interlocked.Read(ref _pausedUntilTicks))
        {
            return DeezerLookup<T>.Unavailable;
        }

        try
        {
            Statistics.DeezerApiCalls.Inc();
            using var response = await this._httpClient.GetAsync(path);

            if (!response.IsSuccessStatusCode)
            {
                Log.Warning("DeezerService: HTTP {statusCode} for {path}", (int)response.StatusCode, path);
                return DeezerLookup<T>.Unavailable;
            }

            await using var stream = await response.Content.ReadAsStreamAsync();
            using var document = await JsonDocument.ParseAsync(stream);
            var root = document.RootElement;

            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("error", out var error))
            {
                var code = error.TryGetProperty("code", out var codeElement) && codeElement.TryGetInt32(out var parsedCode)
                    ? parsedCode
                    : 0;

                switch (code)
                {
                    case NoDataErrorCode:
                        return DeezerLookup<T>.NotFound;
                    case QuotaErrorCode:
                        Interlocked.Exchange(ref _pausedUntilTicks, DateTime.UtcNow.Add(QuotaPause).Ticks);
                        Log.Warning("DeezerService: Quota exceeded, pausing for {pause}", QuotaPause);
                        return DeezerLookup<T>.Unavailable;
                    default:
                        Log.Warning("DeezerService: API error {code} for {path}: {message}", code, path,
                            error.TryGetProperty("message", out var message) ? message.GetString() : null);
                        return DeezerLookup<T>.Unavailable;
                }
            }

            var item = root.Deserialize<T>(JsonOptions);
            return item == null ? DeezerLookup<T>.NotFound : DeezerLookup<T>.Found(item);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException)
        {
            Log.Warning(e, "DeezerService: Request failed for {path}", path);
            return DeezerLookup<T>.Unavailable;
        }
    }
}
