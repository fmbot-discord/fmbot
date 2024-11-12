using System.Net.Http;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using static FMBot.Bot.Models.OpenAIModels;
using FMBot.Domain.Models;
using Microsoft.Extensions.Options;
using System;
using System.Globalization;
using System.Text;
using FMBot.Bot.Builders;
using FMBot.Bot.Extensions;
using FMBot.Bot.Models;
using FMBot.Domain;
using FMBot.Domain.Enums;
using FMBot.Domain.Types;
using FMBot.Persistence.Domain.Models;
using FMBot.Persistence.EntityFrameWork;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Serilog;

namespace FMBot.Bot.Services;

public class OpenAiService
{
    private readonly HttpClient _httpClient;
    private readonly BotSettings _botSettings;
    private readonly IDbContextFactory<FMBotDbContext> _contextFactory;
    private readonly IMemoryCache _cache;

    public OpenAiService(HttpClient httpClient, IOptions<BotSettings> botSettings,
        IDbContextFactory<FMBotDbContext> contextFactory, IMemoryCache cache)
    {
        this._httpClient = httpClient;
        this._contextFactory = contextFactory;
        this._cache = cache;
        this._botSettings = botSettings.Value;
    }

    private async Task<OpenAiResponse> SendRequest(string prompt, string model = "gpt-4o-mini",
        string userMessage = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions");
        request.Headers.Add("Authorization", $"Bearer {this._botSettings.OpenAi.Key}");

        var content = new OpenAiRequest
        {
            Model = model,
            Messages = new List<RequestMessage>
            {
                new()
                {
                    Role = "system",
                    Content = prompt
                }
            }
        };

        if (userMessage != null)
        {
            content.Messages.Add(new RequestMessage
            {
                Role = "user",
                Content = userMessage
            });
        }

        var requestContent = JsonSerializer.Serialize(content);

        request.Content = new StringContent(requestContent, null, "application/json");
        var response = await this._httpClient.SendAsync(request);
        Statistics.OpenAiCalls.Inc();

        var responseContent = await response.Content.ReadAsStringAsync();

        var responseModel = JsonSerializer.Deserialize<OpenAiResponse>(responseContent);
        responseModel.Prompt = prompt;

        return responseModel;
    }

    public async Task<OpenAiResponse> GetJudgeResponse(List<string> artists, PromptType promptType,
        bool supporter = false, string language = "en-us")
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        var prompt = await db.AiPrompts
            .OrderByDescending(o => o.Version)
            .FirstAsync(f => f.Type == promptType &&
                             f.Language == language);

        var artistList = new List<string>();
        foreach (var artist in artists)
        {
            artistList.Add(artist[..Math.Min(artist.Length, 36)]);
        }

        var model = supporter ? prompt.PremiumModel : prompt.FreeModel;

        return await SendRequest($"{prompt.Prompt} {string.Join(", ", artistList)}", model);
    }

    public async Task<AiGeneration> StoreAiGeneration(OpenAiResponse response, int userId, int? targetedUserId)
    {
        var generation = new AiGeneration
        {
            DateGenerated = DateTime.UtcNow,
            Model = response.Model,
            Output = response.Choices?.FirstOrDefault()?.ChoiceMessage?.Content,
            TotalTokens = response.Usage.TotalTokens,
            Prompt = response.Prompt,
            UserId = userId,
            TargetedUserId = targetedUserId
        };

        await using var db = await this._contextFactory.CreateDbContextAsync();

        await db.AiGenerations.AddAsync(generation);

        await db.SaveChangesAsync();

        return generation;
    }

    public async Task<int> GetJudgeUsesLeft(User user)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();

        var filterDate = DateTime.UtcNow.Date;
        var generatedToday =
            await db.AiGenerations.CountAsync(c => c.UserId == user.UserId && c.DateGenerated >= filterDate);

        var dailyAmount = user.UserType != UserType.User ? 15 : 3;
        return dailyAmount - generatedToday;
    }

    public async Task<bool> CheckIfUsernameOffensive(string username)
    {
        try
        {
            var response =
                await SendRequest($"Is the username '{username}' offensive? Only reply with 'true' or 'false'.");

            var output = response.Choices.FirstOrDefault()?.ChoiceMessage?.Content;
            return output != null && output.ToLower().Contains("true");
        }
        catch (Exception e)
        {
            Log.Error(e, "Feature: Error in OpenAI call");
            return false;
        }
    }

    public bool RecapCacheHot(string timePeriod, string lastFmUserName)
    {
        return this._cache.TryGetValue($"{lastFmUserName}-recap-{timePeriod}", out _);
    }

    public async Task<string> GetPlayRecap(string timePeriod, List<UserPlay> userPlays, string lastFmUserName,
        Response<TopArtistList> topArtists)
    {
        try
        {
            var cacheKey = $"{lastFmUserName}-recap-{timePeriod}";
            if (this._cache.TryGetValue(cacheKey, out string cachedResponse))
            {
                return cachedResponse;
            }

            await using var db = await this._contextFactory.CreateDbContextAsync();
            var prompt = await db.AiPrompts
                .OrderByDescending(o => o.Version)
                .FirstAsync(f => f.Type == PromptType.Recap);

            prompt.Prompt = prompt.Prompt.Replace("{{recapType}}", timePeriod);

            var promptBuilder = new StringBuilder();

            promptBuilder.AppendLine("Top 80 artists");
            foreach (var topArtist in topArtists.Content.TopArtists.Take(80))
            {
                promptBuilder.AppendLine(
                    $"{StringExtensions.TruncateLongString(topArtist.ArtistName, 28)}, {topArtist.UserPlaycount} plays");
            }

            if (userPlays.Count > 100)
            {
                var topAlbums = userPlays
                    .Where(w => w.AlbumName != null)
                    .GroupBy(g => new
                    {
                        ArtistName = g.ArtistName.ToLower(),
                        AlbumName = g.AlbumName.ToLower()
                    })
                    .OrderByDescending(o => o.Count())
                    .Take(40)
                    .ToList();

                promptBuilder.AppendLine("---");
                promptBuilder.AppendLine("Top 40 albums");
                foreach (var topAlbum in topAlbums)
                {
                    promptBuilder.AppendLine(
                        $"{StringExtensions.TruncateLongString(topAlbum.Key.AlbumName, 32)} by {StringExtensions.TruncateLongString(topAlbum.Key.ArtistName, 28)}, " +
                        $"{topAlbum.Count()} plays");
                }

                var topTracks = userPlays
                    .GroupBy(g => new
                    {
                        ArtistName = g.ArtistName.ToLower(),
                        TrackName = g.TrackName.ToLower()
                    })
                    .OrderByDescending(o => o.Count())
                    .Take(40)
                    .ToList();

                promptBuilder.AppendLine("---");
                promptBuilder.AppendLine("Top 40 tracks");
                foreach (var topTrack in topTracks)
                {
                    promptBuilder.AppendLine(
                        $"{StringExtensions.TruncateLongString(topTrack.Key.TrackName, 32)} by {StringExtensions.TruncateLongString(topTrack.Key.ArtistName, 28)}, " +
                        $"{topTrack.Count()} plays");
                }
            }

            Log.Information(promptBuilder.ToString());

            var response = await SendRequest(prompt.Prompt, userMessage: promptBuilder.ToString());

            var output = response.Choices.FirstOrDefault()?.ChoiceMessage?.Content;

            this._cache.Set(cacheKey, output, TimeSpan.FromHours(2));

            return output;
        }
        catch (Exception e)
        {
            Log.Error(e, "Feature: Error in OpenAI call");
            return null;
        }
    }
}
