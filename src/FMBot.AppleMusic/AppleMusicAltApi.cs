using System.Text.Json;
using System.Text.Json.Serialization;
using FMBot.AppleMusic.Converters;
using FMBot.AppleMusic.Models;
using Microsoft.AspNetCore.WebUtilities;
using Serilog;

namespace FMBot.AppleMusic;

public class AppleMusicAltApi
{
    private readonly HttpClient _client;

    public AppleMusicAltApi(HttpClient client)
    {
        this._client = client;
    }

    private static JsonSerializerOptions GetJsonSerializerOptions()
    {
        return new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters =
            {
                new JsonStringEnumConverter(JsonNamingPolicy.CamelCase),
                new StringToIntConverter()
            }
        };
    }

    public async Task<AmArtist> GetArtistAsync(string artistId)
    {
        var queryParams = new Dictionary<string, string>
        {
            { "extend", "editorialVideo" }
        };

        var response = await this._client.GetAsync(QueryHelpers.AddQueryString($"artists/{artistId}", queryParams));
        var requestBody = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            Log.Error("AppleMusicAltApi: Bad HTTP status code in GetArtistAsync - {statusCode} - {requestBody}", response.StatusCode, requestBody);
            return null;
        }

        return JsonSerializer.Deserialize<AmArtist>(requestBody, GetJsonSerializerOptions());
    }

    public async Task<AmAlbum> GetAlbumAsync(string albumId)
    {
        var queryParams = new Dictionary<string, string>
        {
            { "extend", "editorialVideo" }
        };

        var response = await this._client.GetAsync(QueryHelpers.AddQueryString($"albums/{albumId}", queryParams));
        var requestBody = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            Log.Error("AppleMusicAltApi: Bad HTTP status code in GetAlbumAsync - {statusCode} - {requestBody}", response.StatusCode, requestBody);
            return null;
        }

        return JsonSerializer.Deserialize<AmAlbum>(requestBody, GetJsonSerializerOptions());
    }
}
