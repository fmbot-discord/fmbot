using System;
using System.Diagnostics;
using System.Net.Http;
using System.Threading.Tasks;
using DiscogsClient;
using Discord.Commands;
using FMBot.Domain.Models;
using Microsoft.Extensions.Options;
using RestSharpHelper.OAuth1;

namespace FMBot.Bot.Services.ThirdParty;

public class DiscogsService
{
    private readonly BotSettings _botSettings;

    public DiscogsService(IOptions<BotSettings> botSettings)
    {
        this._botSettings = botSettings.Value;
    }

    //public async Task<string> GetDiscogsAuthUrl()
    //{

    //}

    //private Task<string> ExtractVerifier(string arg)
    //{


    //}


    public async Task AuthDiscogsTextCommand(ICommandContext context)
    {
        var oAuthConsumerInformation = new OAuthConsumerInformation("lOFKIklDEtHmeJAHQExo", "xGJqCDwxhzOwmNNXUVBjfiQdxyoouKEO");
        var discogsClient = new DiscogsAuthentifierClient(oAuthConsumerInformation);

        var aouth = discogsClient.Authorize(s => Task.FromResult(GetToken(s, context))).Result;

        Console.WriteLine($"{((aouth != null) ? "Success" : "Fail")}");
        Console.WriteLine($"Token:{aouth?.TokenInformation?.Token}, Token:{aouth?.TokenInformation?.TokenSecret}");
    }

    private static string GetToken(string url, ICommandContext commandContext)
    {

        Console.WriteLine("Please authourize the application and enter the final key in the console");
        Process.Start(url);
        string tokenKey = Console.ReadLine();
        tokenKey = string.IsNullOrEmpty(tokenKey) ? null : tokenKey;
        return tokenKey;
    }
}
