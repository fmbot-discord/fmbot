using System.Text;
using System.Threading.Tasks;
using Discord;
using Discord.Interactions;
using Fergun.Interactive;
using FMBot.Bot.Builders;
using FMBot.Bot.Extensions;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;

namespace FMBot.Bot.SlashCommands;

public class StaticSlashCommands : InteractionModuleBase
{
    private readonly UserService _userService;
    private readonly StaticBuilders _staticBuilders;

    private InteractiveService Interactivity { get; }


    public StaticSlashCommands(UserService userService, StaticBuilders staticBuilders, InteractiveService interactivity)
    {
        this._userService = userService;
        this._staticBuilders = staticBuilders;
        this.Interactivity = interactivity;
    }

    [SlashCommand("outofsync", "Shows info if your Last.fm isn't up to date with Spotify")]
    public async Task OutOfSyncAsync([Summary("private", "Show info privately?")]bool privateResponse = true)
    {
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var response = StaticBuilders.OutOfSync(new ContextModel(this.Context, contextUser));

        await this.Context.SendResponse(this.Interactivity, response, ephemeral: privateResponse);
        this.Context.LogCommandUsed(response.CommandResponse);
    }

    [SlashCommand("getsupporter", "Information about getting supporter or your current subscription")]
    public async Task GetSupporterAsync()
    {
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var response = await this._staticBuilders.DonateAsync(new ContextModel(this.Context, contextUser));

        await this.Context.SendResponse(this.Interactivity, response);
        this.Context.LogCommandUsed(response.CommandResponse);
    }

    [SlashCommand("supporters", "Shows all current supporters")]
    public async Task SupportersAsync()
    {
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var response = await this._staticBuilders.SupportersAsync(new ContextModel(this.Context, contextUser));

        await this.Context.SendResponse(this.Interactivity, response);
        this.Context.LogCommandUsed(response.CommandResponse);
    }
}
