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

    public async Task<OpenAiResponse> GetResponse(List<string> artists, bool compliment)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions");
        request.Headers.Add("Authorization", $"Bearer {this._botSettings.OpenAi.Key}");

        var prompt = compliment ? this._botSettings.OpenAi.ComplimentPrompt : this._botSettings.OpenAi.RoastPrompt;

        var artistList = new List<string>();
        foreach (var artist in artists)
        {
             artistList.Add(artist[..Math.Min(artist.Length, 28)]);
        }

        var content = new OpenAiRequest
        {
            Model = "gpt-3.5-turbo",
            Messages = new List<RequestMessage>
            {
                new()
                {
                    Role = "system",
                    Content = $"{prompt} {string.Join(", ", artistList)}"
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

    public async Task<int> GetAmountGeneratedToday(int userId)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();

        var filterDate = DateTime.UtcNow.Date;
        return await db.AiGenerations.CountAsync(c => c.UserId == userId && c.DateGenerated >= filterDate);
    }
}
