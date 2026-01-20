using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using FMBot.Domain.Models;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Serilog;

namespace FMBot.Bot.Services.ThirdParty;

public class GeniusService
{
    private readonly BotSettings _botSettings;
    private readonly HttpClient _client;
    private const string BaseUrl = "https://api.genius.com/";

    public GeniusService(IOptions<BotSettings> botSettings, HttpClient httpClient)
    {
        this._botSettings = botSettings.Value;
        this._client = httpClient;
    }

    public async Task<List<SearchHit>> SearchGeniusAsync(string searchValue, string currentTrackName = null,
        string currentTrackArtist = null)
    {
        var queryParams = new Dictionary<string, string>
        {
            { "q", searchValue }
        };

        var url = BaseUrl + "search";
        url = QueryHelpers.AddQueryString(url, queryParams);

        var request = new HttpRequestMessage
        {
            RequestUri = new Uri(url),
            Method = HttpMethod.Get
        };

        request.Headers.Add("Authorization", $"Bearer {this._botSettings.Genius.AccessToken}");

        try
        {
            using var httpResponse = await this._client.SendAsync(request);

            if (!httpResponse.IsSuccessStatusCode)
            {
                return null;
            }

            var stream = await httpResponse.Content.ReadAsStreamAsync();
            using var streamReader = new StreamReader(stream);
            var requestBody = await streamReader.ReadToEndAsync();

            var jsonSerializerOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            var result = JsonSerializer.Deserialize<GeniusResponse>(requestBody, jsonSerializerOptions);

            if (result?.Response?.Hits == null || !result.Response.Hits.Any())
            {
                return null;
            }

            var results = result.Response.Hits
                .Where(w =>
                    w.Result.PrimaryArtist != null &&
                    !w.Result.PrimaryArtist.Name.Contains("Spotify", StringComparison.CurrentCultureIgnoreCase) &&
                    !w.Result.PrimaryArtist.Name.Contains("Genius", StringComparison.CurrentCultureIgnoreCase))
                .OrderByDescending(o => o.Result.PyongsCount.HasValue)
                .ThenByDescending(o => o.Result.PyongsCount)
                .ToList();

            if (currentTrackName != null && currentTrackArtist != null)
            {
                results = results.Where(w =>
                    w.Result.FullTitle.Contains(currentTrackName, StringComparison.CurrentCultureIgnoreCase) ||
                    w.Result.FullTitle.Contains(currentTrackArtist, StringComparison.CurrentCultureIgnoreCase) ||
                    w.Result.TitleWithFeatured.Contains(currentTrackName, StringComparison.CurrentCultureIgnoreCase) ||
                    w.Result.TitleWithFeatured.Contains(currentTrackArtist,
                        StringComparison.CurrentCultureIgnoreCase) ||
                    currentTrackName.Contains(w.Result.FullTitle, StringComparison.CurrentCultureIgnoreCase) ||
                    currentTrackArtist.Contains(w.Result.FullTitle, StringComparison.CurrentCultureIgnoreCase) ||
                    currentTrackName.Contains(w.Result.TitleWithFeatured, StringComparison.CurrentCultureIgnoreCase) ||
                    currentTrackArtist.Contains(w.Result.TitleWithFeatured, StringComparison.CurrentCultureIgnoreCase)
                ).ToList();
            }

            return results;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Something went wrong while deserializing the response from the Genius API");
            return null;
        }
    }

    private class GeniusResponse
    {
        [JsonPropertyName("response")]
        public GeniusResponseContent Response { get; set; }
    }

    private class GeniusResponseContent
    {
        [JsonPropertyName("hits")]
        public List<SearchHit> Hits { get; set; }
    }

    public class SearchHit
    {
        [JsonPropertyName("type")]
        public string Type { get; set; }

        [JsonPropertyName("result")]
        public SearchResult Result { get; set; }
    }

    public class SearchResult
    {
        [JsonPropertyName("full_title")]
        public string FullTitle { get; set; }

        [JsonPropertyName("title_with_featured")]
        public string TitleWithFeatured { get; set; }

        [JsonPropertyName("url")]
        public string Url { get; set; }

        [JsonPropertyName("song_art_image_thumbnail_url")]
        public string SongArtImageThumbnailUrl { get; set; }

        [JsonPropertyName("pyongs_count")]
        public int? PyongsCount { get; set; }

        [JsonPropertyName("primary_artist")]
        public Artist PrimaryArtist { get; set; }
    }

    public class Artist
    {
        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("url")]
        public string Url { get; set; }
    }
}
