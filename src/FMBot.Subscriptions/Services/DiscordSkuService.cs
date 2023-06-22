using System.Text.Json;
using System.Text.Json.Serialization;
using FMBot.Subscriptions.Models;
using Microsoft.Extensions.Configuration;
using Serilog;

namespace FMBot.Subscriptions.Services;

public class DiscordSkuService
{
    private readonly HttpClient _client;

    private readonly string _baseUrl;

    private readonly string _token;
    private readonly string _appId;

    public DiscordSkuService(IConfiguration configuration, HttpClient client)
    {
        this._token = configuration.GetSection("Discord:Token").Value;
        this._appId = configuration.GetSection("Discord:ApplicationId").Value;

        this._baseUrl = "https://discord.com/api/v10/";

        this._client = client;
    }

    public async Task<List<DiscordEntitlement>> GetEntitlements()
    {
        var url = this._baseUrl + $"applications/{this._appId}/entitlements";

        var request = new HttpRequestMessage
        {
            RequestUri = new Uri(url),
            Method = HttpMethod.Get,
        };

        request.Headers.Add("Authorization", $"Bot {this._token}");

        try
        {
            using var httpResponse = await this._client.SendAsync(request);

            if (!httpResponse.IsSuccessStatusCode)
            {
                Log.Error("DiscordEntitlements: HTTP status code {statusCode} - {reason}", httpResponse.StatusCode, httpResponse.ReasonPhrase);
            }

            var stream = await httpResponse.Content.ReadAsStreamAsync();
            using var streamReader = new StreamReader(stream);
            var requestBody = await streamReader.ReadToEndAsync();

            var jsonSerializerOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                NumberHandling = JsonNumberHandling.AllowReadingFromString
            };

            var result = JsonSerializer.Deserialize<List<DiscordEntitlementResponseModel>>(requestBody, jsonSerializerOptions);

            return result
                .Where(w => w.UserId.HasValue)
                .Select(ResponseToModel)
                .ToList();
        }
        catch (Exception ex)
        {
            Log.Error("Something went wrong while deserializing Discord entitlements", ex);
            return null;
        }
    }

    private DiscordEntitlement ResponseToModel(DiscordEntitlementResponseModel response)
    {
        return new DiscordEntitlement
        {
            DiscordUserId = response.UserId.Value,
            Active = !response.EndsAt.HasValue || response.EndsAt.Value > DateTime.UtcNow,
            StartsAt = response.StartsAt.HasValue ? DateTime.SpecifyKind(response.StartsAt.Value, DateTimeKind.Utc) : null,
            EndsAt = response.EndsAt.HasValue ? DateTime.SpecifyKind(response.EndsAt.Value, DateTimeKind.Utc) : null
        };
    }
}
