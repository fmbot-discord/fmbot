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
using NetCord.Services.ComponentInteractions;


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
        [SlashCommandParameter(Name = "User", Description = "The user to show (defaults to self)")] string user = null)
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

    [UsernameSetRequired]
    [ComponentInteraction($"{InteractionConstants.Genre.GenreGuild}~*~*~*~*")]
    public async Task GuildGenresAsync(string discordUser, string requesterDiscordUser, string genre, string originalSearch)
    {
        await RespondAsync(InteractionCallback.DeferredMessage());

        var message = (this.Context.Interaction as MessageComponentInteraction)?.Message;
        if (message == null)
        {
            return;
        }

        var components =
            new ActionRowProperties().WithButton($"Loading server view...", customId: "1", emote: EmojiProperties.Custom("<a:loading:821676038102056991>"), disabled: true, style: ButtonStyle.Secondary);
        await message.ModifyAsync(m => m.Components = [components]);

        var discordUserId = ulong.Parse(discordUser);
        var requesterDiscordUserId = ulong.Parse(requesterDiscordUser);
        var originalSearchValue = originalSearch == "0" ? null : originalSearch;

        var guild = await this._guildService.GetGuildAsync(this.Context.Guild.Id);
        var contextUser = await this._userService.GetUserWithDiscogs(requesterDiscordUserId);
        var discordContextUser = await this.Context.Client.GetUserAsync(requesterDiscordUserId);
        var userSettings = await this._settingService.GetOriginalContextUser(discordUserId, requesterDiscordUserId, this.Context.Guild, this.Context.User);

        try
        {
            var response = await this._genreBuilders.GenreAsync(new ContextModel(this.Context, contextUser, discordContextUser), genre, userSettings, guild, false, originalSearchValue);

            await this.Context.UpdateInteractionEmbed(response, this.Interactivity, false);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [UsernameSetRequired]
    [ComponentInteraction($"{InteractionConstants.Genre.GenreUser}~*~*~*~*")]
    public async Task UserGenresAsync(string discordUser, string requesterDiscordUser, string genre, string originalSearch)
    {
        await RespondAsync(InteractionCallback.DeferredMessage());

        var message = (this.Context.Interaction as MessageComponentInteraction)?.Message;
        if (message == null)
        {
            return;
        }

        var components =
            new ActionRowProperties().WithButton($"Loading user view...", customId: "1", emote: EmojiProperties.Custom("<a:loading:821676038102056991>"), disabled: true, style: ButtonStyle.Secondary);
        await Context.ModifyComponents(message, components);

        var discordUserId = ulong.Parse(discordUser);
        var requesterDiscordUserId = ulong.Parse(requesterDiscordUser);
        var originalSearchValue = originalSearch == "0" ? null : originalSearch;

        var guild = await this._guildService.GetGuildAsync(this.Context.Guild.Id);
        var contextUser = await this._userService.GetUserWithDiscogs(requesterDiscordUserId);
        var discordContextUser = await this.Context.Client.GetUserAsync(requesterDiscordUserId);
        var userSettings = await this._settingService.GetOriginalContextUser(discordUserId, requesterDiscordUserId, this.Context.Guild, this.Context.User);

        try
        {
            var context = new ContextModel(this.Context, contextUser, discordContextUser);
            var response = await this._genreBuilders.GenreAsync(context, genre, userSettings, guild, originalSearch: originalSearchValue);

            await this.Context.UpdateInteractionEmbed(response, this.Interactivity, false);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [ComponentInteraction(InteractionConstants.Genre.GenreSelectMenu)]
    [UsernameSetRequired]
    public async Task SetResponseModeAsync(string[] inputs)
    {
        try
        {
            var options = inputs.First().Split("~");

            await RespondAsync(InteractionCallback.DeferredMessage());

            var message = (this.Context.Interaction as MessageComponentInteraction)?.Message;
            if (message == null)
            {
                return;
            }

            var discordUserId = ulong.Parse(options[0]);
            var requesterDiscordUserId = ulong.Parse(options[1]);
            var view = options[2];
            var selectedOption = options[3];
            var originalSearch = string.IsNullOrWhiteSpace(options[4]) ? null : options[4];

            var components =
                new ActionRowProperties().WithButton($"Loading {selectedOption}...", customId: "1", emote: EmojiProperties.Custom(DiscordConstants.Loading), disabled: true, style: ButtonStyle.Secondary);
            await Context.ModifyComponents(message, components);

            var guild = await this._guildService.GetGuildAsync(this.Context.Guild?.Id);
            var contextUser = await this._userService.GetUserWithFriendsAsync(requesterDiscordUserId);
            var discordContextUser = await this.Context.Client.GetUserAsync(requesterDiscordUserId);
            var userSettings = await this._settingService.GetOriginalContextUser(discordUserId, requesterDiscordUserId, this.Context.Guild, this.Context.User);

            var context = new ContextModel(this.Context, contextUser, discordContextUser);

            ResponseModel response;

            switch (view)
            {
                default:
                    response = await this._genreBuilders.GenreAsync(context, selectedOption, userSettings, guild, originalSearch: originalSearch);
                    break;
                case "guild-genre":
                    response = await this._genreBuilders.GenreAsync(context, selectedOption, userSettings, guild, false, originalSearch: originalSearch);
                    break;
                case "whoknows":
                    response = await this._genreBuilders.WhoKnowsGenreAsync(context, selectedOption, originalSearch: originalSearch);
                    break;
                case "friendwhoknows":
                    response = await this._genreBuilders.FriendsWhoKnowsGenreAsync(context, selectedOption, originalSearch: originalSearch);
                    break;

            }

            await this.Context.UpdateInteractionEmbed(response, this.Interactivity, false);
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
        [SlashCommandParameter(Name = "Private", Description = "Only show response to you")] bool privateResponse = false)
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
