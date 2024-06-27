using System.Text.Json;
using FMBot.AppleMusic.Models;

namespace FMBot.AppleMusic;

public class AppleMusicApi
{
    private readonly HttpClient _client;

    public AppleMusicApi(HttpClient client)
    {
        this._client = client;
    }

    public async Task<AmArtist> GetArtistAsync(string artistId)
    {
        var response = await this._client.GetAsync($"artists/{artistId}");
        response.EnsureSuccessStatusCode();

        var requestBody = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<AmArtist>(requestBody, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    }
}
