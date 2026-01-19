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

public class GenreInteractions(
    UserService userService,
    GuildService guildService,
    GenreBuilders genreBuilders,
    SettingService settingService,
    InteractiveService interactivity)
    : ComponentInteractionModule<ComponentInteractionContext>
{
    [UsernameSetRequired]
    [ComponentInteraction(InteractionConstants.Genre.GenreGuild)]
    public async Task GuildGenresAsync(string discordUser, string requesterDiscordUser, string genre, string originalSearch)
    {
        await RespondAsync(InteractionCallback.DeferredModifyMessage);

        var message = (this.Context.Interaction as MessageComponentInteraction)?.Message;
        if (message == null)
        {
            return;
        }

        var components =
            new ActionRowProperties().WithButton("Loading server view...", "1", ButtonStyle.Secondary, emote: EmojiProperties.Custom(DiscordConstants.Loading), disabled: true);
        await message.ModifyAsync(m => m.Components = [components]);

        var discordUserId = ulong.Parse(discordUser);
        var requesterDiscordUserId = ulong.Parse(requesterDiscordUser);
        var originalSearchValue = originalSearch == "0" ? null : originalSearch;

        var guild = await guildService.GetGuildAsync(this.Context.Guild.Id);
        var contextUser = await userService.GetUserWithDiscogs(requesterDiscordUserId);
        var discordContextUser = await this.Context.GetUserAsync(requesterDiscordUserId);
        var userSettings = await settingService.GetOriginalContextUser(discordUserId, requesterDiscordUserId, this.Context.Guild, this.Context.User);

        try
        {
            var response = await genreBuilders.GenreAsync(new ContextModel(this.Context, contextUser, discordContextUser), genre, userSettings, guild, false, originalSearchValue);

            await this.Context.UpdateInteractionEmbed(response, interactivity, false);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [UsernameSetRequired]
    [ComponentInteraction(InteractionConstants.Genre.GenreUser)]
    public async Task UserGenresAsync(string discordUser, string requesterDiscordUser, string genre, string originalSearch)
    {
        await RespondAsync(InteractionCallback.DeferredModifyMessage);

        var message = (this.Context.Interaction as MessageComponentInteraction)?.Message;
        if (message == null)
        {
            return;
        }

        var components =
            new ActionRowProperties().WithButton("Loading user view...", "1", ButtonStyle.Secondary, emote: EmojiProperties.Custom(DiscordConstants.Loading), disabled: true);
        await Context.ModifyComponents(message, components);

        var discordUserId = ulong.Parse(discordUser);
        var requesterDiscordUserId = ulong.Parse(requesterDiscordUser);
        var originalSearchValue = originalSearch == "0" ? null : originalSearch;

        var guild = await guildService.GetGuildAsync(this.Context.Guild.Id);
        var contextUser = await userService.GetUserWithDiscogs(requesterDiscordUserId);
        var discordContextUser = await this.Context.GetUserAsync(requesterDiscordUserId);
        var userSettings = await settingService.GetOriginalContextUser(discordUserId, requesterDiscordUserId, this.Context.Guild, this.Context.User);

        try
        {
            var context = new ContextModel(this.Context, contextUser, discordContextUser);
            var response = await genreBuilders.GenreAsync(context, genre, userSettings, guild, originalSearch: originalSearchValue);

            await this.Context.UpdateInteractionEmbed(response, interactivity, false);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [ComponentInteraction(InteractionConstants.Genre.GenreSelectMenu)]
    [UsernameSetRequired]
    public async Task SetResponseModeAsync()
    {
        try
        {
            var stringMenuInteraction = (StringMenuInteraction)this.Context.Interaction;
            var options = stringMenuInteraction.Data.SelectedValues.First().Split(":");

            await RespondAsync(InteractionCallback.DeferredModifyMessage);

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
                new ActionRowProperties().WithButton($"Loading {selectedOption}...", "1", ButtonStyle.Secondary, emote: EmojiProperties.Custom(DiscordConstants.Loading), disabled: true);
            await Context.ModifyComponents(message, components);

            var guild = await guildService.GetGuildAsync(this.Context.Guild?.Id);
            var contextUser = await userService.GetUserWithFriendsAsync(requesterDiscordUserId);
            var discordContextUser = await this.Context.GetUserAsync(requesterDiscordUserId);
            var userSettings = await settingService.GetOriginalContextUser(discordUserId, requesterDiscordUserId, this.Context.Guild, this.Context.User);

            var context = new ContextModel(this.Context, contextUser, discordContextUser);

            ResponseModel response;

            switch (view)
            {
                default:
                    response = await genreBuilders.GenreAsync(context, selectedOption, userSettings, guild, originalSearch: originalSearch);
                    break;
                case "guild-genre":
                    response = await genreBuilders.GenreAsync(context, selectedOption, userSettings, guild, false, originalSearch: originalSearch);
                    break;
                case "whoknows":
                    response = await genreBuilders.WhoKnowsGenreAsync(context, selectedOption, originalSearch: originalSearch);
                    break;
                case "friendwhoknows":
                    response = await genreBuilders.FriendsWhoKnowsGenreAsync(context, selectedOption, originalSearch: originalSearch);
                    break;

            }

            await this.Context.UpdateInteractionEmbed(response, interactivity, false);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }
}
