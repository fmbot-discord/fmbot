using System;
using System.Threading.Tasks;
using Fergun.Interactive;
using FMBot.Bot.Attributes;
using FMBot.Bot.Builders;
using FMBot.Bot.Extensions;
using FMBot.Bot.Interfaces;
using FMBot.Bot.Models;
using FMBot.Bot.Services;
using FMBot.Domain.Models;
using Microsoft.Extensions.Options;
using NetCord.Services.Commands;

namespace FMBot.Bot.TextCommands;

[ModuleName("Youtube")]
public class YoutubeCommands(
    IPrefixService prefixService,
    UserService userService,
    IOptions<BotSettings> botSettings,
    InteractiveService interactivity,
    YoutubeBuilders youtubeBuilders)
    : BaseCommandModule(botSettings)
{
    private InteractiveService Interactivity { get; } = interactivity;


    [Command("youtube", "yt", "y", "youtubesearch", "ytsearch", "yts")]
    [Summary("Shares a link to a YouTube video based on what a user is listening to or searching for")]
    [UsernameSetRequired]
    [CommandCategories(CommandCategory.ThirdParty)]
    public async Task YoutubeAsync([CommandParameter(Remainder = true)] string searchValue = null)
    {
        try
        {
            var contextUser = await userService.GetUserSettingsAsync(this.Context.User);
            var prfx = prefixService.GetPrefix(this.Context.Guild?.Id);

            _ = this.Context.Channel?.TriggerTypingStateAsync()!;

            if (this.Context.Message.ReferencedMessage != null && string.IsNullOrWhiteSpace(searchValue))
            {
                var internalLookup =
                    CommandContextExtensions.GetReferencedMusic(this.Context.Message.ReferencedMessage.Id)
                    ??
                    await userService.GetReferencedMusic(this.Context.Message.ReferencedMessage.Id);

                if (internalLookup?.Track != null)
                {
                    searchValue = $"{internalLookup.Artist} | {internalLookup.Track}";
                }
            }

            var response =
                await youtubeBuilders.YoutubeAsync(new ContextModel(this.Context, prfx, contextUser),
                    searchValue);

            await this.Context.SendResponse(this.Interactivity, response, userService);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }
}
