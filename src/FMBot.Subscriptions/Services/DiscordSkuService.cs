using System.Text.Json;
using System.Text.Json.Serialization;
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

    public async Task<List<DiscordEntitlementResponseModel>> GetEntitlementsFromDiscord(ulong? discordUserId = null, ulong? after = null, ulong? before = null)
    {
        var fetchEntitlements = this._baseUrl + $"applications/{this._appId}/entitlements";

        var request = new HttpRequestMessage
        {
            RequestUri = new Uri(fetchEntitlements),
            Method = HttpMethod.Get
        };

        request.Headers.Add("Authorization", $"Bot {this._token}");

        try
        {
            if (discordUserId != null || before != null || after != null)
            {
                var queryParams = new Dictionary<string, string>();

                if (discordUserId != null)
                {
                    queryParams.Add("user_id", discordUserId.ToString());
                }

                if (after != null)
                {
                    queryParams.Add("after", after.ToString());
                }

                if (before != null)
                {
                    queryParams.Add("before", before.ToString());
                }

                request.RequestUri = new Uri(QueryHelpers.AddQueryString(fetchEntitlements, queryParams));
            }

            var result = await GetDiscordEntitlements(request);

            Log.Information("Found {entitlementsCount} Discord entitlements", result.Count);

            return result;
        }
        catch (Exception ex)
        {
            Log.Error("Something went wrong while deserializing Discord entitlements", ex);
            return null;
        }
    }

    public async Task<List<DiscordEntitlement>> GetEntitlements(ulong? discordUserId = null, ulong? after = null, ulong? before = null)
    {
        var discordEntitlements = await GetEntitlementsFromDiscord(discordUserId, after, before);

        return DiscordEntitlementsToGrouped(discordEntitlements);
    }

    public static List<DiscordEntitlement> DiscordEntitlementsToGrouped(IEnumerable<DiscordEntitlementResponseModel> discordEntitlements)
    {
        return discordEntitlements
            .Where(w => w.UserId.HasValue)
            .GroupBy(g => g.UserId.Value)
            .Select(DiscordEntitlement)
            .ToList();
    }

    private static DiscordEntitlement DiscordEntitlement(IGrouping<ulong, DiscordEntitlementResponseModel> s)
    {
        return new DiscordEntitlement
        {
            DiscordUserId = s.OrderByDescending(o => o.EndsAt).First().UserId.Value,
            Active = !s.OrderByDescending(o => o.EndsAt).First().EndsAt.HasValue || s.OrderByDescending(o => o.EndsAt).First().EndsAt.Value > DateTime.UtcNow.AddDays(-2),
            StartsAt = s.OrderByDescending(o => o.EndsAt).First().StartsAt.HasValue ? DateTime.SpecifyKind(s.OrderByDescending(o => o.EndsAt).First().StartsAt.Value, DateTimeKind.Utc) : null,
            EndsAt = s.OrderByDescending(o => o.EndsAt).First().EndsAt.HasValue ? DateTime.SpecifyKind(s.OrderByDescending(o => o.EndsAt).First().EndsAt.Value, DateTimeKind.Utc) : null
        };
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
