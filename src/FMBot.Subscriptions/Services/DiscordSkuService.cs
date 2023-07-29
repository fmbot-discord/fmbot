using System.Text.Json;
using System.Text.Json.Serialization;
using FMBot.Domain.Types;
using FMBot.Subscriptions.Models;
using Microsoft.AspNetCore.WebUtilities;
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
        var fetchEntitlements = this._baseUrl + $"applications/{this._appId}/entitlements";

        var request = new HttpRequestMessage
        {
            RequestUri = new Uri(fetchEntitlements),
            Method = HttpMethod.Get,
        };

        request.Headers.Add("Authorization", $"Bot {this._token}");

        try
        {
            var result = await GetDiscordEntitlements(request);

            if (result.Count >= 100)
            {
                var queryParams = new Dictionary<string, string>
                {
                    { "after", "100" }
                };

                request.RequestUri = new Uri(QueryHelpers.AddQueryString(fetchEntitlements, queryParams));

                var secondResult = await GetDiscordEntitlements(request);

                if (secondResult.Any())
                {
                    var existingIds = result.Select(x => x.Id).ToHashSet();
                    result.AddRange(secondResult.Where(w => !existingIds.Contains(w.Id)));
                }
            }

            Log.Information("Found {entitlementsCount} Discord entitlements", result.Count);

            return result
                .Where(w => w.UserId.HasValue)
                .GroupBy(g => g.UserId.Value)
                .Select(s => new DiscordEntitlement
                {
                    DiscordUserId = s.OrderByDescending(o => o.EndsAt).First().UserId.Value,
                    Active = !s.OrderByDescending(o => o.EndsAt).First().EndsAt.HasValue || s.OrderByDescending(o => o.EndsAt).First().EndsAt.Value > DateTime.UtcNow.AddDays(-7),
                    StartsAt = s.OrderByDescending(o => o.EndsAt).First().StartsAt.HasValue ? DateTime.SpecifyKind(s.OrderByDescending(o => o.EndsAt).First().StartsAt.Value, DateTimeKind.Utc) : null,
                    EndsAt = s.OrderByDescending(o => o.EndsAt).First().EndsAt.HasValue ? DateTime.SpecifyKind(s.OrderByDescending(o => o.EndsAt).First().EndsAt.Value, DateTimeKind.Utc) : null
                })
                .ToList();
        }
        catch (Exception ex)
        {
            Log.Error("Something went wrong while deserializing Discord entitlements", ex);
            return null;
        }

    }

    async Task<List<DiscordEntitlementResponseModel>> GetDiscordEntitlements(HttpRequestMessage httpRequestMessage)
    {
        using var httpResponse = await this._client.SendAsync(httpRequestMessage);

        if (!httpResponse.IsSuccessStatusCode)
        {
            Log.Error("DiscordEntitlements: HTTP status code {statusCode} - {reason}", httpResponse.StatusCode,
                httpResponse.ReasonPhrase);
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
        return result;
    }
}
