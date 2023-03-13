using System.Net.Http;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using static FMBot.Bot.Models.OpenAIModels;
using FMBot.Domain.Models;
using Microsoft.Extensions.Options;
using System;

namespace FMBot.Bot.Services;

public class OpenAiService
{
    private readonly HttpClient _httpClient;
    private readonly BotSettings _botSettings;

    public OpenAiService(HttpClient httpClient, IOptions<BotSettings> botSettings)
    {
        this._httpClient = httpClient;
        this._botSettings = botSettings.Value;
    }

    public async Task<string> GetResponse(List<string> artists, bool compliment)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions");
        request.Headers.Add("Authorization", $"Bearer {this._botSettings.OpenAi.Key}");

        var prompt = compliment ? this._botSettings.OpenAi.ComplimentPrompt : this._botSettings.OpenAi.RoastPrompt;

        var artistList = new List<string>();
        foreach (var artist in artists)
        {
             artistList.Add(artist[..Math.Min(artist.Length, 24)]);
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

        var responseContent = await response.Content.ReadAsStringAsync();

        var responseModel = JsonSerializer.Deserialize<OpenAiResponse>(responseContent);

        return responseModel?.Choices?.FirstOrDefault()?.ChoiceMessage?.Content;
    }
}
