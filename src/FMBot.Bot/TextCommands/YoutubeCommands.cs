using System;
using System.Threading.Tasks;

using Discord.Commands;
using FMBot.Bot.Attributes;
using FMBot.Bot.Builders;
using FMBot.Bot.Extensions;
using FMBot.Bot.Interfaces;
using FMBot.Bot.Models;
using FMBot.Bot.Services;
using FMBot.Bot.Services.ThirdParty;
using FMBot.Domain;
using FMBot.Domain.Interfaces;
using FMBot.Domain.Models;
using Microsoft.Extensions.Options;

namespace FMBot.Bot.TextCommands;

[Name("Youtube")]
public class YoutubeCommands : BaseCommandModule
{
    private readonly UserService _userService;

    private readonly IPrefixService _prefixService;

    private readonly YoutubeBuilders _youtubeBuilders;
    private InteractiveService Interactivity { get; }


    public YoutubeCommands(
        IPrefixService prefixService,
        UserService userService,
        IOptions<BotSettings> botSettings, InteractiveService interactivity,
        YoutubeBuilders youtubeBuilders) : base(botSettings)
    {
        this._prefixService = prefixService;
        this._userService = userService;
        this.Interactivity = interactivity;
        this._youtubeBuilders = youtubeBuilders;
    }

    [Command("youtube")]
    [Summary("Shares a link to a YouTube video based on what a user is listening to or searching for")]
    [Alias("yt", "y", "youtubesearch", "ytsearch", "yts")]
    [UsernameSetRequired]
    [CommandCategories(CommandCategory.ThirdParty)]
    public async Task YoutubeAsync([Remainder] string searchValue = null)
    {
        try
        {
            var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);

            _ = this.Context.Channel?.TriggerTypingStateAsync()!;

            if (this.Context.Message.ReferencedMessage != null && string.IsNullOrWhiteSpace(searchValue))
            {
                var internalLookup =
                    CommandContextExtensions.GetReferencedMusic(this.Context.Message.ReferencedMessage.Id)
                    ??
                    await this._userService.GetReferencedMusic(this.Context.Message.ReferencedMessage.Id);

                if (internalLookup?.Track != null)
                {
                    searchValue = $"{internalLookup.Artist} | {internalLookup.Track}";
                }
            }

            var response =
                await this._youtubeBuilders.YoutubeAsync(new ContextModel(this.Context, prfx, contextUser),
                    searchValue);

            await this.Context.SendResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }
}
