using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Fergun.Interactive;
using FMBot.Bot.Attributes;
using FMBot.Bot.AutoCompleteHandlers;
using FMBot.Bot.Builders;
using FMBot.Bot.Extensions;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Bot.Services.Guild;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;


namespace FMBot.Bot.SlashCommands;

public class GenreSlashCommands : ApplicationCommandModule<ApplicationCommandContext>
{
    private readonly UserService _userService;
    private readonly GuildService _guildService;
    private readonly GenreBuilders _genreBuilders;
    private readonly SettingService _settingService;

    private InteractiveService Interactivity { get; }

    public GenreSlashCommands(UserService userService, InteractiveService interactivity, GenreBuilders genreBuilders, GuildService guildService, SettingService settingService)
    {
        this._userService = userService;
        this.Interactivity = interactivity;
        this._genreBuilders = genreBuilders;
        this._guildService = guildService;
        this._settingService = settingService;
    }

    [SlashCommand("genre", "Shows genre info for artist or top artists for genre", Contexts = [InteractionContextType.BotDMChannel, InteractionContextType.DMChannel, InteractionContextType.Guild], IntegrationTypes = [ApplicationIntegrationType.UserInstall, ApplicationIntegrationType.GuildInstall])]
    [UsernameSetRequired]
    public async Task GenreAsync(
        [SlashCommandParameter(Name = "search", Description = "The genre or artist you want to view", AutocompleteProviderType = typeof(GenreArtistAutoComplete))] string search = null,
        [SlashCommandParameter(Name = "user", Description = "The user to show (defaults to self)")] string user = null)
    {
        await RespondAsync(InteractionCallback.DeferredMessage());

        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var guild = await this._guildService.GetGuildAsync(this.Context.Guild?.Id);
        var userSettings = await this._settingService.GetUser(user, contextUser, this.Context.Guild, this.Context.User, true);

        try
        {
            var response = await this._genreBuilders.GenreAsync(new ContextModel(this.Context, contextUser), search, userSettings, guild);

            await this.Context.SendFollowUpResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [SlashCommand("fwkgenre", "Shows who of your friends listen to a genre", Contexts = [InteractionContextType.BotDMChannel, InteractionContextType.DMChannel, InteractionContextType.Guild], IntegrationTypes = [ApplicationIntegrationType.UserInstall, ApplicationIntegrationType.GuildInstall])]
    [UsernameSetRequired]
    [RequiresIndex]
    public async Task FriendsWhoKnowGenreAsync(
        [SlashCommandParameter(Name = "search", Description = "The genre or artist you want to view", AutocompleteProviderType = typeof(GenreArtistAutoComplete))] string search = null,
        [SlashCommandParameter(Name = "private", Description = "Only show response to you")] bool privateResponse = false)
    {
        await Context.Interaction.SendResponseAsync(InteractionCallback.DeferredMessage(privateResponse ? MessageFlags.Ephemeral : default));

        var contextUser = await this._userService.GetUserWithFriendsAsync(this.Context.User);

        try
        {
            var response = await this._genreBuilders.FriendsWhoKnowsGenreAsync(new ContextModel(this.Context, contextUser), search);

            await this.Context.SendFollowUpResponse(this.Interactivity, response, privateResponse);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }
}
