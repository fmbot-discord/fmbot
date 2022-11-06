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

public class GenreSlashCommands : InteractionModuleBase
{
    private readonly UserService _userService;
    private readonly GuildService _guildService;
    private readonly GenreBuilders _genreBuilders;

    private InteractiveService Interactivity { get; }

    public GenreSlashCommands(UserService userService, InteractiveService interactivity, GenreBuilders genreBuilders, GuildService guildService)
    {
        this._userService = userService;
        this.Interactivity = interactivity;
        this._genreBuilders = genreBuilders;
        this._guildService = guildService;
    }

    //[SlashCommand("genre", "Shows genre info for artist or top artists for genre")]
    [UsernameSetRequired]
    public async Task GenreAsync(
        [Summary("search", "The genre or artist you want to view")]
        [Autocomplete(typeof(GenreArtistAutoComplete))]
        string name = null)
    {
        _ = DeferAsync();

        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var guild = await this._guildService.GetGuildAsync(this.Context.Guild.Id);

        try
        {
            var response = await this._genreBuilders.GenreAsync(new ContextModel(this.Context, contextUser), name, guild);

            await this.Context.SendResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }
}
