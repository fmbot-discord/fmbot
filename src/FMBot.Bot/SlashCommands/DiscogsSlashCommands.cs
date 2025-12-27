using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using Fergun.Interactive;
using FMBot.Bot.Attributes;
using FMBot.Bot.Builders;
using FMBot.Bot.Extensions;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Domain.Enums;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;
using NetCord.Services.ComponentInteractions;

namespace FMBot.Bot.SlashCommands;

public class DiscogsSlashCommands : ApplicationCommandModule<ApplicationCommandContext>
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

    [SlashCommand("discogs", "Connects your Discogs account by sending a link to your DMs", Contexts = [InteractionContextType.BotDMChannel, InteractionContextType.DMChannel, InteractionContextType.Guild], IntegrationTypes = [ApplicationIntegrationType.UserInstall, ApplicationIntegrationType.GuildInstall])]
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
                    var serverEmbed = new EmbedProperties()
                        .WithColor(DiscordConstants.InformationColorBlue);

                    serverEmbed.WithDescription("Check your DMs for a link to connect your Discogs account to .fmbot!");
                    await this.Context.Interaction.SendResponseAsync(InteractionCallback.Message(new InteractionMessageProperties()
                        .WithEmbeds([serverEmbed])
                        .WithFlags(MessageFlags.Ephemeral)));
                }

                var response = this._discogsBuilder.DiscogsLoginGetLinkAsync(new ContextModel(this.Context, contextUser));
                var dmChannel = await this.Context.User.GetDMChannelAsync();
                await dmChannel.SendMessageAsync(new MessageProperties
                {
                    Embeds = [response.Embed],
                    Components = [response.Components]
                });
                this.Context.LogCommandUsed(response.CommandResponse);
            }
            else
            {
                if (this.Context.Guild != null)
                {
                    var serverEmbed = new EmbedProperties()
                        .WithColor(DiscordConstants.InformationColorBlue);

                    serverEmbed.WithDescription("Check your DMs for a message to manage your connected Discogs account!");
                    await this.Context.Interaction.SendResponseAsync(InteractionCallback.Message(new InteractionMessageProperties()
                        .WithEmbeds([serverEmbed])
                        .WithFlags(MessageFlags.Ephemeral)));
                }

                var response = DiscogsBuilder.DiscogsManage(new ContextModel(this.Context, contextUser));
                var manageDmChannel = await this.Context.User.GetDMChannelAsync();
                await manageDmChannel.SendMessageAsync(new MessageProperties
                {
                    Embeds = [response.Embed],
                    Components = [response.Components]
                });
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

        var message = (this.Context.Interaction as MessageComponentInteraction)?.Message;

        if (message == null)
        {
            return;
        }

        var embed = new EmbedProperties();
        embed.WithDescription("Fetching login link...");
        embed.WithColor(DiscordConstants.InformationColorBlue);
        await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
            .WithEmbeds([embed])));

        var components = new ActionRowProperties();
        await message.ModifyAsync(m => m.Components = components);

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

    [SlashCommand("collection", "You or someone else's Discogs collection", Contexts = [InteractionContextType.BotDMChannel, InteractionContextType.DMChannel, InteractionContextType.Guild], IntegrationTypes = [ApplicationIntegrationType.UserInstall, ApplicationIntegrationType.GuildInstall])]
    [UsernameSetRequired]
    public async Task AlbumAsync(
        [SlashCommandParameter(Name = "Search", Description = "Search query to filter on")] string search = null,
        [SlashCommandParameter(Name = "User", Description = "The user to show (defaults to self)")] string user = null,
        [SlashCommandParameter(Name = "Format", Description = "Media format to include")] DiscogsFormat? format = null)
    {
        await RespondAsync(InteractionCallback.DeferredMessage());

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

    [ComponentInteraction($"{InteractionConstants.Discogs.Collection}-*-*")]
    public async Task CollectionAsync(string discordUser, string requesterDiscordUser)
    {
        await RespondAsync(InteractionCallback.DeferredMessage());
        await this.Context.DisableActionRows(specificButtonOnly:$"{InteractionConstants.Discogs.Collection}-{discordUser}-{requesterDiscordUser}");

        var discordUserId = ulong.Parse(discordUser);
        var requesterDiscordUserId = ulong.Parse(requesterDiscordUser);

        var contextUser = await this._userService.GetUserWithDiscogs(requesterDiscordUserId);
        var discordContextUser = await this.Context.Client.GetUserAsync(requesterDiscordUserId);
        var userSettings = await this._settingService.GetOriginalContextUser(discordUserId, requesterDiscordUserId, this.Context.Guild, this.Context.User);

        var collectionSettings = new DiscogsCollectionSettings
        {
            Formats = new()
        };

        try
        {
            var response = await this._discogsBuilder.DiscogsCollectionAsync(new ContextModel(this.Context, contextUser, discordContextUser), userSettings, collectionSettings, null);

            await this.Context.SendFollowUpResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }
}
