using System.Net.Http;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using static FMBot.Bot.Models.OpenAIModels;
using FMBot.Domain.Models;
using Microsoft.Extensions.Options;
using System;
using FMBot.Domain;
using FMBot.Persistence.Domain.Models;
using FMBot.Persistence.EntityFrameWork;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace FMBot.Bot.Services;

public class OpenAiService
{
    private readonly HttpClient _httpClient;
    private readonly BotSettings _botSettings;
    private readonly IDbContextFactory<FMBotDbContext> _contextFactory;

    public OpenAiService(HttpClient httpClient, IOptions<BotSettings> botSettings, IDbContextFactory<FMBotDbContext> contextFactory)
    {
        this._httpClient = httpClient;
        this._contextFactory = contextFactory;
        this._botSettings = botSettings.Value;
    }

    private async Task<OpenAiResponse> SendRequest(string prompt, string model = "gpt-3.5-turbo")
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

        var requestContent = JsonSerializer.Serialize(content);

        request.Content = new StringContent(requestContent, null, "application/json");
        var response = await this._httpClient.SendAsync(request);
        Statistics.OpenAiCalls.Inc();

        var responseContent = await response.Content.ReadAsStringAsync();

        var responseModel = JsonSerializer.Deserialize<OpenAiResponse>(responseContent);
        responseModel.Prompt = prompt;

        return responseModel;
    }

    public async Task<OpenAiResponse> GetResponse(List<string> artists, bool compliment)
    {
        var prompt = compliment ? this._botSettings.OpenAi.ComplimentPrompt : this._botSettings.OpenAi.RoastPrompt;

        var artistList = new List<string>();
        foreach (var artist in artists)
        {
             artistList.Add(artist[..Math.Min(artist.Length, 34)]);
        }

        return await SendRequest($"{prompt} {string.Join(", ", artistList)}");
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

    public async Task<int> GetCommandUsesLeft(User user)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();

        var filterDate = DateTime.UtcNow.Date;
        var generatedToday = await db.AiGenerations.CountAsync(c => c.UserId == user.UserId && c.DateGenerated >= filterDate);

        var dailyAmount = user.UserType != UserType.User ? 25 : 3;
        return dailyAmount - generatedToday;
    }

    public async Task<bool> CheckIfUsernameOffensive(string username)
    {
        try
        {
            var response = await SendRequest($"Is the username '{username}' offensive? Only reply with 'true' or 'false'.");

            var output = response.Choices.FirstOrDefault()?.ChoiceMessage?.Content;
            return output != null && output.ToLower().Contains("true");
        }
        catch (Exception e)
        {
            Log.Error(e, "Feature: Error in OpenAI call");
            return false;
        }
    }
}
