using System;
using System.Threading.Tasks;

using FMBot.Bot.Attributes;
using FMBot.Bot.Builders;
using FMBot.Bot.Extensions;
using FMBot.Bot.Models;
using FMBot.Bot.Services;
using FMBot.Bot.Services.ThirdParty;
using FMBot.Domain.Interfaces;
using NetCord.Services.ApplicationCommands;
using NetCord;
using NetCord.Rest;
using Fergun.Interactive;
using NetCord.Services.Commands;

namespace FMBot.Bot.SlashCommands;

public class YoutubeSlashCommands: ApplicationCommandModule<ApplicationCommandContext>
{
    private readonly UserService _userService;
    private readonly YoutubeBuilders _youtubeBuilders;

    private InteractiveService Interactivity { get; }


    public YoutubeSlashCommands(UserService userService, InteractiveService interactivity, YoutubeBuilders youtubeBuilders)
    {
        this._userService = userService;
        this.Interactivity = interactivity;
        this._youtubeBuilders = youtubeBuilders;
    }

    [SlashCommand("youtube", "Search through YouTube", Contexts = [InteractionContextType.BotDMChannel, InteractionContextType.DMChannel,     InteractionContextType.Guild], IntegrationTypes = [ApplicationIntegrationType.UserInstall, ApplicationIntegrationType.GuildInstall])]
    [UsernameSetRequired]
    public async Task YoutubeAsync(
        [SlashCommandParameter(Name = "search", Description = "Search value")] string searchValue = null,
        [SlashCommandParameter(Name = "private", Description = "Only show response to you")] bool privateResponse = false)
    {
        try
        {
            var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

            var response =
                await this._youtubeBuilders.YoutubeAsync(new ContextModel(this.Context, contextUser), searchValue);

            await this.Context.SendResponse(this.Interactivity, response, privateResponse);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }
}
