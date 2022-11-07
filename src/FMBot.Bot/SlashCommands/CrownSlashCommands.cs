using System;
using System.Threading.Tasks;
using Discord.Interactions;
using Fergun.Interactive;
using FMBot.Bot.Attributes;
using FMBot.Bot.AutoCompleteHandlers;
using FMBot.Bot.Builders;
using FMBot.Bot.Extensions;
using FMBot.Bot.Models;
using FMBot.Bot.Services;
using FMBot.Bot.Services.Guild;

namespace FMBot.Bot.SlashCommands;

public class CrownSlashCommands : InteractionModuleBase
{
    private readonly CrownBuilders _crownBuilders;
    private readonly UserService _userService;
    private readonly GuildService _guildService;

    private InteractiveService Interactivity { get; }


    public CrownSlashCommands(CrownBuilders crownBuilders, InteractiveService interactivity, UserService userService, GuildService guildService)
    {
        this._crownBuilders = crownBuilders;
        this.Interactivity = interactivity;
        this._userService = userService;
        this._guildService = guildService;
    }

    [SlashCommand("crown", "Shows history for a specific crown")]
    [UsernameSetRequired]
    public async Task AlbumAsync(
        [Summary("Artist", "The artist your want to search for (defaults to currently playing)")]
        [Autocomplete(typeof(ArtistAutoComplete))] string name = null)
    {
        _ = DeferAsync();

        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var guild = await this._guildService.GetGuildWithGuildUsers(this.Context.Guild.Id);

        try
        {
            var response = await this._crownBuilders.CrownAsync(new ContextModel(this.Context, contextUser), guild, name);

            await this.Context.SendFollowUpResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }
}
