using FMBot.Bot.Configurations;
using Google.Protobuf.WellKnownTypes;
using Serilog;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System;
using System.Text.Json;
using Web.InternalApi;

namespace FMBot.Bot.Services.ThirdParty;

public class BotListService
{
    private readonly StatusHandler.StatusHandlerClient _statusHandler;
    private readonly HttpClient _httpClient;

    public BotListService(StatusHandler.StatusHandlerClient statusHandler, HttpClient httpClient)
    {
        this._statusHandler = statusHandler;
        this._httpClient = httpClient;
    }

    public async Task UpdateBotLists()
    {
        if (ConfigData.Data.BotLists?.TopGgApiToken == null ||
            ConfigData.Data.BotLists?.BotsForDiscordToken == null ||
            ConfigData.Data.BotLists?.BotsOnDiscordToken == null)
        {
            return;
        }

        var currentProcess = Process.GetCurrentProcess();
        var startTime = DateTime.Now - currentProcess.StartTime;

        if (startTime.Minutes <= 30)
        {
            Log.Information($"Skipping {nameof(UpdateBotLists)} because bot only just started");
            return;
        }

        Log.Information($"{nameof(UpdateBotLists)}: Starting");
        const string requestUri = "https://botblock.org/api/count";

        var overview = await this._statusHandler.GetOverviewAsync(new Empty());
        var postData = new Dictionary<string, object>
        {
            { "server_count",  overview.TotalGuilds },
            { "bot_id", "356268235697553409" },
            { "top.gg", ConfigData.Data.BotLists.TopGgApiToken },
            { "discords.com", ConfigData.Data.BotLists.BotsForDiscordToken },
            { "bots.ondiscord.xyz", ConfigData.Data.BotLists.BotsOnDiscordToken }
        };

        var json = JsonSerializer.Serialize(postData);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await this._httpClient.PostAsync(requestUri, content);
        Log.Information(response.IsSuccessStatusCode
            ? $"{nameof(UpdateBotLists)}: Updated successfully"
            : $"{nameof(UpdateBotLists)}: Failed to post data. Status code: {response.StatusCode}");
    }
}
