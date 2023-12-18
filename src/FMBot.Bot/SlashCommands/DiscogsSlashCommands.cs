using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Fergun.Interactive;
using FMBot.Bot.Attributes;
using FMBot.Bot.Builders;
using FMBot.Bot.Extensions;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Domain.Enums;

namespace FMBot.Bot.SlashCommands;

public class DiscogsSlashCommands : InteractionModuleBase
{
    private readonly UserService _userService;
    private readonly DiscogsBuilder _discogsBuilder;
    private readonly SettingService _settingService;

    private InteractiveService Interactivity { get; }


    public DiscogsSlashCommands(UserService userService, DiscogsBuilder discogsBuilder, InteractiveService interactivity, SettingService settingService)
    {
        this._userService = userService;
        this._discogsBuilder = discogsBuilder;
        this.Interactivity = interactivity;
        this._settingService = settingService;
    }

    [SlashCommand("discogs", "Connects your Discogs account by sending a link to your DMs")]
    [UsernameSetRequired]
    public async Task DiscogsAsync()
    {
        var contextUser = await this._userService.GetUserWithDiscogs(this.Context.User.Id);

        try
        {
            if (contextUser.UserDiscogs == null)
            {
                if (this.Context.Guild != null)
                {
                    var serverEmbed = new EmbedBuilder()
                        .WithColor(DiscordConstants.InformationColorBlue);

                    serverEmbed.WithDescription("Check your DMs for a link to connect your Discogs account to .fmbot!");
                    await this.Context.Interaction.RespondAsync("", embed: serverEmbed.Build(), ephemeral: true);
                }

                var response = this._discogsBuilder.DiscogsLoginGetLinkAsync(new ContextModel(this.Context, contextUser));
                await this.Context.User.SendMessageAsync("", false, response.Embed.Build(), components: response.Components.Build());
                this.Context.LogCommandUsed(response.CommandResponse);
            }
            else
            {
                if (this.Context.Guild != null)
                {
                    var serverEmbed = new EmbedBuilder()
                        .WithColor(DiscordConstants.InformationColorBlue);

                    serverEmbed.WithDescription("Check your DMs for a message to manage your connected Discogs account!");
                    await this.Context.Interaction.RespondAsync("", embed: serverEmbed.Build(), ephemeral: true);
                }

                var response = DiscogsBuilder.DiscogsManage(new ContextModel(this.Context, contextUser));
                await this.Context.User.SendMessageAsync("", false, response.Embed.Build(), components: response.Components.Build());
                this.Context.LogCommandUsed(response.CommandResponse);
            }
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [ComponentInteraction(InteractionConstants.Discogs.AuthDm)]
    [UsernameSetRequired]
    public async Task SendAuthDm()
    {
        var contextUser = await this._userService.GetUserWithDiscogs(this.Context.User.Id);

        try
        {
            var response = this._discogsBuilder.DiscogsLoginGetLinkAsync(new ContextModel(this.Context, contextUser));
            await this.Context.SendResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [ComponentInteraction(InteractionConstants.Discogs.ToggleCollectionValue)]
    [UsernameSetRequired]
    public async Task ToggleCollectionValue()
    {
        var contextUser = await this._userService.GetUserWithDiscogs(this.Context.User.Id);

        try
        {
            var response = await this._discogsBuilder.DiscogsToggleCollectionValue(new ContextModel(this.Context, contextUser));
            await this.Context.SendResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);

            contextUser = await this._userService.GetUserWithDiscogs(this.Context.User.Id);
            var updatedMsg = DiscogsBuilder.DiscogsManage(new ContextModel(this.Context, contextUser));
            await this.Context.UpdateInteractionEmbed(updatedMsg, this.Interactivity, false);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [ComponentInteraction(InteractionConstants.Discogs.RemoveAccount)]
    [UsernameSetRequired]
    public async Task RemoveDiscogsLogin()
    {
        await this.Context.DisableInteractionButtons();
        var contextUser = await this._userService.GetUserWithDiscogs(this.Context.User.Id);

        try
        {
            var response = await this._discogsBuilder.DiscogsRemove(new ContextModel(this.Context, contextUser));
            await this.Context.SendResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [ComponentInteraction(InteractionConstants.Discogs.StartAuth)]
    public async Task DiscogsStartAuthAsync()
    {
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

        var message = (this.Context.Interaction as SocketMessageComponent)?.Message;

        if (message == null)
        {
            return;
        }

        var embed = new EmbedBuilder();
        embed.WithDescription("Fetching login link...");
        embed.WithColor(DiscordConstants.InformationColorBlue);
        await RespondAsync(embed: embed.Build());

        var components = new ComponentBuilder();
        await message.ModifyAsync(m => m.Components = components.Build());

        try
        {
            var response = await this._discogsBuilder.DiscogsLoginAsync(new ContextModel(this.Context, contextUser));
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [SlashCommand("collection", "Shows your or someone else their Discogs collection")]
    [UsernameSetRequired]
    public async Task AlbumAsync(
        [Summary("Search", "Search query to filter on")] string search = null,
        [Summary("User", "The user to show (defaults to self)")] string user = null,
        [Summary("Format", "Media format to include")] DiscogsFormat? format = null)
    {
        _ = DeferAsync();

        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var userSettings = await this._settingService.GetUser(user, contextUser, this.Context.Guild, this.Context.User, true);
        var collectionSettings = new DiscogsCollectionSettings
        {
            Formats = format != null ? new List<DiscogsFormat> { (DiscogsFormat)format } : new()
        };

        try
        {
            var response = await this._discogsBuilder.DiscogsCollectionAsync(new ContextModel(this.Context, contextUser), userSettings, collectionSettings, search);

            await this.Context.SendFollowUpResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }
}
