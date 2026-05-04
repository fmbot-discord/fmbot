using System.Text.Json;
using System.Text.Json.Serialization;
using FMBot.AppleMusic.Converters;
using FMBot.AppleMusic.Models;
using Microsoft.AspNetCore.WebUtilities;
using Serilog;

namespace FMBot.AppleMusic;

public class AppleMusicApi(HttpClient client)
{
    private static JsonSerializerOptions GetJsonSerializerOptions()
    {
        return new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters =
            {
                new JsonStringEnumConverter(JsonNamingPolicy.CamelCase),
                new StringToLongConverter()
            }
        };
    }

    public async Task<AmArtist> GetArtistAsync(string artistId)
    {
        var response = await client.GetAsync($"artists/{artistId}");
        var requestBody = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            Log.Error("AppleMusicApi: Bad HTTP status code in GetArtistAsync - {statusCode} - {requestBody}", response.StatusCode, requestBody);
            return null;
        }

        return JsonSerializer.Deserialize<AmArtist>(requestBody, GetJsonSerializerOptions());
    }

    public async Task<AmAlbum> GetAlbumAsync(string albumId)
    {
        var response = await client.GetAsync($"albums/{albumId}");
        var requestBody = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            Log.Error("AppleMusicApi: Bad HTTP status code in GetAlbumAsync - {statusCode} - {requestBody}", response.StatusCode, requestBody);
            return null;
        }

        return JsonSerializer.Deserialize<AmAlbum>(requestBody, GetJsonSerializerOptions());
    }

    public async Task<AmSong> GetSongAsync(string songId)
    {
        var response = await client.GetAsync($"songs/{songId}");
        var requestBody = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            Log.Error("AppleMusicApi: Bad HTTP status code in GetSongAsync - {statusCode} - {requestBody}", response.StatusCode, requestBody);
            return null;
        }

        return JsonSerializer.Deserialize<AmSong>(requestBody, GetJsonSerializerOptions());
    }

    public async Task<List<AmData<AmArtistAttributes>>> SearchArtistAsync(string searchQuery)
    {
        var queryParams = new Dictionary<string, string>
        {
            { "types", "artists" },
            { "term", searchQuery }
        };

        var response = await client.GetAsync(QueryHelpers.AddQueryString("search", queryParams));
        var requestBody = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            Log.Error("AppleMusicApi: Bad HTTP status code in SearchArtistAsync - {statusCode} - {requestBody}", response.StatusCode, requestBody);
            return null;
        }

        var results = JsonSerializer.Deserialize<AmSearchResult>(requestBody, GetJsonSerializerOptions());
        return results.Results.Artists?.Data;
    }

    public async Task<List<AmData<AmAlbumAttributes>>> SearchAlbumAsync(string searchQuery)
    {
        var queryParams = new Dictionary<string, string>
        {
            { "types", "albums" },
            { "term", searchQuery }
        };

        var response = await client.GetAsync(QueryHelpers.AddQueryString("search", queryParams));
        var requestBody = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            Log.Error("AppleMusicApi: Bad HTTP status code in SearchAlbumAsync - {statusCode} - {requestBody}", response.StatusCode, requestBody);
            return null;
        }

        var results = JsonSerializer.Deserialize<AmSearchResult>(requestBody, GetJsonSerializerOptions());
        return results.Results.Albums?.Data;
    }

    public async Task<List<AmData<AmSongAttributes>>> SearchSongAsync(string searchQuery)
    {
        var queryParams = new Dictionary<string, string>
        {
            { "types", "songs" },
            { "term", searchQuery }
        };

        var response = await client.GetAsync(QueryHelpers.AddQueryString("search", queryParams));
        var requestBody = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            Log.Error("AppleMusicApi: Bad HTTP status code in SearchSongAsync - {statusCode} - {requestBody}", response.StatusCode, requestBody);
            return null;
        }

        var results = JsonSerializer.Deserialize<AmSearchResult>(requestBody, GetJsonSerializerOptions());
        return results.Results.Songs?.Data;
    }
}
