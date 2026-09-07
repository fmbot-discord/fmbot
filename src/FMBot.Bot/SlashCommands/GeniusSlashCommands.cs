using System;
using System.Threading.Tasks;
using Fergun.Interactive;
using FMBot.Bot.Attributes;
using FMBot.Bot.Builders;
using FMBot.Bot.Extensions;
using FMBot.Bot.Models;
using FMBot.Bot.Services;
using NetCord;
using NetCord.Services.ApplicationCommands;

namespace FMBot.Bot.SlashCommands;

public class GeniusSlashCommands(
    GeniusBuilders geniusBuilders,
    UserService userService,
    InteractiveService interactivity)
    : ApplicationCommandModule<ApplicationCommandContext>
{
    private InteractiveService Interactivity { get; } = interactivity;

    [SlashCommand("genius", "Shares a Genius lyrics link for what you're listening to or a track you search for",
        Contexts = [InteractionContextType.BotDMChannel, InteractionContextType.DMChannel, InteractionContextType.Guild],
        IntegrationTypes = [ApplicationIntegrationType.UserInstall, ApplicationIntegrationType.GuildInstall])]
    [UsernameSetRequired]
    public async Task GeniusAsync(
        [SlashCommandParameter(Name = "search", Description = "Track to look up (defaults to what you're playing)")]
        string searchValue = null,
        [SlashCommandParameter(Name = "private", Description = "Only show response to you")]
        bool privateResponse = false)
    {
        this.Context.DeferInBackground(privateResponse ? MessageFlags.Ephemeral : default);

        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);

        try
        {
            var response = await geniusBuilders.GeniusAsync(new ContextModel(this.Context, contextUser), searchValue);

            await this.Context.SendFollowUpResponse(this.Interactivity, response, userService, privateResponse);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }
}
