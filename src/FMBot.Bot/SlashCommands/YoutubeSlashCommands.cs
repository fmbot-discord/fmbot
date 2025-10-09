using System;
using System.Threading.Tasks;
using Discord;
using Discord.Interactions;
using Fergun.Interactive;
using FMBot.Bot.Attributes;
using FMBot.Bot.Builders;
using FMBot.Bot.Extensions;
using FMBot.Bot.Models;
using FMBot.Bot.Services;
using FMBot.Bot.Services.ThirdParty;
using FMBot.Domain.Interfaces;
using NetCord.Services.ApplicationCommands;

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

    [SlashCommand("youtube", "Search through YouTube")]
    [UsernameSetRequired]
    [CommandContextType(InteractionContextType.BotDm, InteractionContextType.PrivateChannel,
        InteractionContextType.Guild)]
    [IntegrationType(ApplicationIntegrationType.UserInstall, ApplicationIntegrationType.GuildInstall)]
    public async Task YoutubeAsync(
        [Summary("Search", "Search value")] string searchValue = null,
        [Summary("Private", "Only show response to you")] bool privateResponse = false)
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
