using System;
using System.Threading.Tasks;
using FMBot.Bot.Attributes;
using FMBot.Bot.Builders;
using FMBot.Bot.Extensions;
using FMBot.Bot.Interfaces;
using FMBot.Bot.Models;
using FMBot.Bot.Services;
using FMBot.Domain.Models;
using Microsoft.Extensions.Options;
using NetCord.Services.Commands;
using Fergun.Interactive;

namespace FMBot.Bot.TextCommands;

[ModuleName("Genius")]
public class GeniusCommands(
    GeniusBuilders geniusBuilders,
    IPrefixService prefixService,
    UserService userService,
    IOptions<BotSettings> botSettings,
    InteractiveService interactivity)
    : BaseCommandModule(botSettings)
{
    private InteractiveService Interactivity { get; } = interactivity;

    [Command("genius", "gen")]
    [Summary("Shares a link to Genius lyrics based on what a user is listening to or searching for")]
    [Examples("genius", "gen", "genius Tame Impala The Less I Know The Better")]
    [UsernameSetRequired]
    [CommandCategories(CommandCategory.ThirdParty)]
    public async Task GeniusAsync([CommandParameter(Remainder = true)] string searchValue = null)
    {
        _ = this.Context.Channel?.TriggerTypingAsync()!;

        var userSettings = await userService.GetUserSettingsAsync(this.Context.User);
        var prfx = prefixService.GetPrefix(this.Context.Guild?.Id);

        try
        {
            if (this.Context.Message.ReferencedMessage != null && string.IsNullOrWhiteSpace(searchValue))
            {
                var internalLookup = CommandContextExtensions.GetReferencedMusic(this.Context.Message.ReferencedMessage.Id)
                                     ??
                                     await userService.GetReferencedMusic(this.Context.Message.ReferencedMessage.Id);

                if (internalLookup?.Track != null)
                {
                    searchValue = $"{internalLookup.Artist} | {internalLookup.Track}";
                }
            }

            var response = await geniusBuilders.GeniusAsync(new ContextModel(this.Context, prfx, userSettings), searchValue);

            await this.Context.SendResponse(this.Interactivity, response, userService);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }
}
