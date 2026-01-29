using System;
using System.Threading.Tasks;
using Fergun.Interactive;
using FMBot.Bot.Attributes;
using FMBot.Bot.Builders;
using FMBot.Bot.Extensions;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Domain;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ComponentInteractions;

namespace FMBot.Bot.Interactions;

public class DiscogsInteractions(
    UserService userService,
    DiscogsBuilder discogsBuilder,
    SettingService settingService,
    InteractiveService interactivity)
    : ComponentInteractionModule<ComponentInteractionContext>
{
    [ComponentInteraction(InteractionConstants.Discogs.AuthDm)]
    [UsernameSetRequired]
    public async Task SendAuthDm()
    {
        var contextUser = await userService.GetUserWithDiscogs(this.Context.User.Id);

        try
        {
            var response = discogsBuilder.DiscogsLoginGetLinkAsync(new ContextModel(this.Context, contextUser));
            await this.Context.SendResponse(interactivity, response, userService);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [ComponentInteraction(InteractionConstants.Discogs.ToggleCollectionValue)]
    [UsernameSetRequired]
    public async Task ToggleCollectionValue()
    {
        var contextUser = await userService.GetUserWithDiscogs(this.Context.User.Id);

        try
        {
            var response = await discogsBuilder.DiscogsToggleCollectionValue(new ContextModel(this.Context, contextUser));
            await this.Context.SendResponse(interactivity, response, userService);
            await this.Context.LogCommandUsedAsync(response, userService);

            contextUser = await userService.GetUserWithDiscogs(this.Context.User.Id);
            var updatedMsg = DiscogsBuilder.DiscogsManage(new ContextModel(this.Context, contextUser));

            var message = (this.Context.Interaction as MessageComponentInteraction)?.Message;
            await message.ModifyAsync(m =>
            {
                m.Components = updatedMsg.GetMessageComponents();
                m.Embeds = [updatedMsg.Embed];
            });
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [ComponentInteraction(InteractionConstants.Discogs.RemoveAccount)]
    [UsernameSetRequired]
    public async Task RemoveDiscogsLogin()
    {
        try
        {
            var message = (this.Context.Interaction as MessageComponentInteraction)?.Message;
            await message.ModifyAsync(m =>
            {
                m.Components = [];
            });

            var contextUser = await userService.GetUserWithDiscogs(this.Context.User.Id);

            var response = await discogsBuilder.DiscogsRemove(new ContextModel(this.Context, contextUser));
            await this.Context.SendResponse(interactivity, response, userService);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [ComponentInteraction(InteractionConstants.Discogs.StartAuth)]
    public async Task DiscogsStartAuthAsync()
    {
        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);

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

        await message.ModifyAsync(m => m.Components = []);

        try
        {
            var response = await discogsBuilder.DiscogsLoginAsync(new ContextModel(this.Context, contextUser));
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [ComponentInteraction(InteractionConstants.Discogs.Collection)]
    public async Task CollectionAsync(string discordUser, string requesterDiscordUser)
    {
        await RespondAsync(InteractionCallback.DeferredModifyMessage);
        await this.Context.DisableButtonsAndMenus(specificButtonOnly: $"{InteractionConstants.Discogs.Collection}:{discordUser}:{requesterDiscordUser}");

        var discordUserId = ulong.Parse(discordUser);
        var requesterDiscordUserId = ulong.Parse(requesterDiscordUser);

        var contextUser = await userService.GetUserWithDiscogs(requesterDiscordUserId);
        var discordContextUser = await this.Context.GetUserAsync(requesterDiscordUserId);
        var userSettings = await settingService.GetOriginalContextUser(discordUserId, requesterDiscordUserId, this.Context.Guild, this.Context.User);

        var collectionSettings = new DiscogsCollectionSettings
        {
            Formats = new()
        };

        try
        {
            var response = await discogsBuilder.DiscogsCollectionAsync(new ContextModel(this.Context, contextUser, discordContextUser), userSettings,
                collectionSettings, null);

            await this.Context.SendFollowUpResponse(interactivity, response, userService);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }
}
