using System;
using System.Threading.Tasks;
using FMBot.Bot.Attributes;
using FMBot.Bot.Builders;
using FMBot.Bot.Extensions;
using FMBot.Bot.Models;
using FMBot.Bot.Services;
using NetCord.Services.ApplicationCommands;
using NetCord;
using Fergun.Interactive;

namespace FMBot.Bot.SlashCommands;

public class YoutubeSlashCommands(
    UserService userService,
    InteractiveService interactivity,
    YoutubeBuilders youtubeBuilders)
    : ApplicationCommandModule<ApplicationCommandContext>
{
    private InteractiveService Interactivity { get; } = interactivity;


    [SlashCommand("youtube", "Search through YouTube",
        Contexts =
            [InteractionContextType.BotDMChannel, InteractionContextType.DMChannel, InteractionContextType.Guild],
        IntegrationTypes = [ApplicationIntegrationType.UserInstall, ApplicationIntegrationType.GuildInstall])]
    [UsernameSetRequired]
    public async Task YoutubeAsync(
        [SlashCommandParameter(Name = "search", Description = "Search value")]
        string searchValue = null,
        [SlashCommandParameter(Name = "private", Description = "Only show response to you")]
        bool privateResponse = false)
    {
        try
        {
            var contextUser = await userService.GetUserSettingsAsync(this.Context.User);

            var response =
                await youtubeBuilders.YoutubeAsync(new ContextModel(this.Context, contextUser), searchValue);

            await this.Context.SendResponse(this.Interactivity, response, userService, privateResponse);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }
}
