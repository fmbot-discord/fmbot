using System.Text.Json;
using System.Text.Json.Serialization;
using FMBot.AppleMusic.Converters;
using FMBot.AppleMusic.Models;

namespace FMBot.AppleMusic;

public class AppleMusicApi
{
    private readonly HttpClient _client;

    public AppleMusicApi(HttpClient client)
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
        var response = await this._client.GetAsync($"artists/{artistId}");
        response.EnsureSuccessStatusCode();

        var requestBody = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<AmArtist>(requestBody, GetJsonSerializerOptions());
    }

    public async Task<AmSong> GetSongAsync(string songId)
    {
        var response = await this._client.GetAsync($"songs/{songId}");
        response.EnsureSuccessStatusCode();

        var requestBody = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<AmSong>(requestBody, GetJsonSerializerOptions());
    }

    public async Task<List<AmData<AmArtistAttributes>>> SearchArtistAsync(string searchQuery)
    {
        var response = await this._client.GetAsync($"search?types=artists&term={searchQuery}");
        response.EnsureSuccessStatusCode();

        var requestBody = await response.Content.ReadAsStringAsync();
        var results = JsonSerializer.Deserialize<AmSearchResult>(requestBody, GetJsonSerializerOptions());

        return results.Results.Artists?.Data;
    }

    public async Task<List<AmData<AmAlbumAttributes>>> SearchAlbumAsync(string searchQuery)
    {
        var response = await this._client.GetAsync($"search?types=albums&term={searchQuery}");
        response.EnsureSuccessStatusCode();

        var requestBody = await response.Content.ReadAsStringAsync();
        var results = JsonSerializer.Deserialize<AmSearchResult>(requestBody, GetJsonSerializerOptions());

        return results.Results.Albums?.Data;
    }

    public async Task<List<AmData<AmSongAttributes>>> SearchSongAsync(string searchQuery)
    {
        var response = await this._client.GetAsync($"search?types=songs&term={searchQuery}");
        response.EnsureSuccessStatusCode();

        var requestBody = await response.Content.ReadAsStringAsync();
        var results = JsonSerializer.Deserialize<AmSearchResult>(requestBody, GetJsonSerializerOptions());

        return results.Results.Songs?.Data;
    }
}
