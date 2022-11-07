using System.Threading.Tasks;
using System;
using Discord.Interactions;
using FMBot.Bot.Attributes;
using FMBot.Bot.AutoCompleteHandlers;
using FMBot.Bot.Builders;
using FMBot.Bot.Extensions;
using FMBot.Bot.Models;
using FMBot.Bot.Services;
using FMBot.Bot.Services.Guild;
using Fergun.Interactive;

namespace FMBot.Bot.SlashCommands;

public class CountrySlashCommands : InteractionModuleBase
{
    private readonly UserService _userService;
    private readonly GuildService _guildService;

    private readonly CountryBuilders _countryBuilders;

    private InteractiveService Interactivity { get; }


    public CountrySlashCommands(UserService userService, CountryBuilders countryBuilders, GuildService guildService, InteractiveService interactivity)
    {
        this._userService = userService;
        this._countryBuilders = countryBuilders;
        this._guildService = guildService;
        this.Interactivity = interactivity;
    }

    [SlashCommand("country", "Shows country for artist or top artists for country")]
    [UsernameSetRequired]
    public async Task GenreAsync(
        [Summary("search", "The country or artist you want to view")]
        [Autocomplete(typeof(CountryArtistAutoComplete))]
        string name = null)
    {
        _ = DeferAsync();

        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

        try
        {
            var response = await this._countryBuilders.CountryAsync(new ContextModel(this.Context, contextUser), name);

            await this.Context.SendFollowUpResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }
}
