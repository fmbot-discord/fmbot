using System;
using System.Linq;
using System.Threading.Tasks;
using Fergun.Interactive;
using FMBot.Bot.Attributes;
using FMBot.Bot.Builders;
using FMBot.Bot.Extensions;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Bot.Services.Guild;
using FMBot.Domain;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ComponentInteractions;

namespace FMBot.Bot.Interactions;

public class GenreInteractions : ComponentInteractionModule<ComponentInteractionContext>
{
    private readonly UserService _userService;
    private readonly GuildService _guildService;
    private readonly GenreBuilders _genreBuilders;
    private readonly SettingService _settingService;
    private readonly InteractiveService _interactivity;

    public GenreInteractions(
        UserService userService,
        GuildService guildService,
        GenreBuilders genreBuilders,
        SettingService settingService,
        InteractiveService interactivity)
    {
        this._userService = userService;
        this._guildService = guildService;
        this._genreBuilders = genreBuilders;
        this._settingService = settingService;
        this._interactivity = interactivity;
    }

    [UsernameSetRequired]
    [ComponentInteraction(InteractionConstants.Genre.GenreGuild)]
    public async Task GuildGenresAsync(string discordUser, string requesterDiscordUser, string genre, string originalSearch)
    {
        await RespondAsync(InteractionCallback.DeferredMessage());

        var message = (this.Context.Interaction as MessageComponentInteraction)?.Message;
        if (message == null)
        {
            return;
        }

        var components =
            new ActionRowProperties().WithButton($"Loading server view...", customId: "1", emote: EmojiProperties.Custom(DiscordConstants.Loading), disabled: true, style: ButtonStyle.Secondary);
        await message.ModifyAsync(m => m.Components = [components]);

        var discordUserId = ulong.Parse(discordUser);
        var requesterDiscordUserId = ulong.Parse(requesterDiscordUser);
        var originalSearchValue = originalSearch == "0" ? null : originalSearch;

        var guild = await this._guildService.GetGuildAsync(this.Context.Guild.Id);
        var contextUser = await this._userService.GetUserWithDiscogs(requesterDiscordUserId);
        var discordContextUser = await this.Context.GetUserAsync(requesterDiscordUserId);
        var userSettings = await this._settingService.GetOriginalContextUser(discordUserId, requesterDiscordUserId, this.Context.Guild, this.Context.User);

        try
        {
            var response = await this._genreBuilders.GenreAsync(new ContextModel(this.Context, contextUser, discordContextUser), genre, userSettings, guild, false, originalSearchValue);

            await this.Context.UpdateInteractionEmbed(response, this._interactivity, false);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [UsernameSetRequired]
    [ComponentInteraction(InteractionConstants.Genre.GenreUser)]
    public async Task UserGenresAsync(string discordUser, string requesterDiscordUser, string genre, string originalSearch)
    {
        await RespondAsync(InteractionCallback.DeferredMessage());

        var message = (this.Context.Interaction as MessageComponentInteraction)?.Message;
        if (message == null)
        {
            return;
        }

        var components =
            new ActionRowProperties().WithButton($"Loading user view...", customId: "1", emote: EmojiProperties.Custom(DiscordConstants.Loading), disabled: true, style: ButtonStyle.Secondary);
        await Context.ModifyComponents(message, components);

        var discordUserId = ulong.Parse(discordUser);
        var requesterDiscordUserId = ulong.Parse(requesterDiscordUser);
        var originalSearchValue = originalSearch == "0" ? null : originalSearch;

        var guild = await this._guildService.GetGuildAsync(this.Context.Guild.Id);
        var contextUser = await this._userService.GetUserWithDiscogs(requesterDiscordUserId);
        var discordContextUser = await this.Context.GetUserAsync(requesterDiscordUserId);
        var userSettings = await this._settingService.GetOriginalContextUser(discordUserId, requesterDiscordUserId, this.Context.Guild, this.Context.User);

        try
        {
            var context = new ContextModel(this.Context, contextUser, discordContextUser);
            var response = await this._genreBuilders.GenreAsync(context, genre, userSettings, guild, originalSearch: originalSearchValue);

            await this.Context.UpdateInteractionEmbed(response, this._interactivity, false);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [ComponentInteraction(InteractionConstants.Genre.GenreSelectMenu)]
    [UsernameSetRequired]
    public async Task SetResponseModeAsync(params string[] inputs)
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
            var discordContextUser = await this.Context.GetUserAsync(requesterDiscordUserId);
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

            await this.Context.UpdateInteractionEmbed(response, this._interactivity, false);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }
}
