using System;
using System.Threading.Tasks;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Fergun.Interactive;
using FMBot.Bot.Attributes;
using FMBot.Bot.AutoCompleteHandlers;
using FMBot.Bot.Builders;
using FMBot.Bot.Extensions;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
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

    [SlashCommand("genre", "Shows genre info for artist or top artists for genre")]
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

            await this.Context.SendFollowUpResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [UsernameSetRequired]
    [ComponentInteraction($"{InteractionConstants.GenreGuild}-*-*")]
    public async Task GuildGenresAsync(string discordUserId, string genre)
    {
        _ = DeferAsync();

        var message = (this.Context.Interaction as SocketMessageComponent)?.Message;
        if (message == null)
        {
            return;
        }

        var components =
            new ComponentBuilder().WithButton($"Loading server view...", customId: "1", emote: Emote.Parse("<a:loading:821676038102056991>"), disabled: true, style: ButtonStyle.Primary);
        await message.ModifyAsync(m => m.Components = components.Build());

        var contextUser = await this._userService.GetUserAsync(ulong.Parse(discordUserId));
        var guild = await this._guildService.GetGuildAsync(this.Context.Guild.Id);

        try
        {
            var response = await this._genreBuilders.GenreAsync(new ContextModel(this.Context, contextUser), genre, guild, false);

            await this.Context.UpdateInteractionEmbed(response, this.Interactivity);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [UsernameSetRequired]
    [ComponentInteraction($"{InteractionConstants.GenreUser}-*-*")]
    public async Task UserGenresAsync(string discordUserId, string genre)
    {
        _ = DeferAsync();

        var message = (this.Context.Interaction as SocketMessageComponent)?.Message;
        if (message == null)
        {
            return;
        }

        var components =
            new ComponentBuilder().WithButton($"Loading user view...", customId: "1", emote: Emote.Parse("<a:loading:821676038102056991>"), disabled: true, style: ButtonStyle.Primary);
        await message.ModifyAsync(m => m.Components = components.Build());

        var contextUser = await this._userService.GetUserAsync(ulong.Parse(discordUserId));
        var guild = await this._guildService.GetGuildAsync(this.Context.Guild.Id);

        try
        {
            var context = new ContextModel(this.Context, contextUser)
            {
                DiscordUser = await this.Context.Guild.GetUserAsync(ulong.Parse(discordUserId))
            };

            var response = await this._genreBuilders.GenreAsync(context, genre, guild);

            await this.Context.UpdateInteractionEmbed(response, this.Interactivity);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }
}
