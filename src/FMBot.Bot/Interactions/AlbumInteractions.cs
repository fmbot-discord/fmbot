using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Fergun.Interactive;
using FMBot.Bot.Attributes;
using FMBot.Bot.Builders;
using FMBot.Bot.Extensions;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Domain;
using FMBot.Domain.Models;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ComponentInteractions;

namespace FMBot.Bot.Interactions;

public class AlbumInteractions(
    UserService userService,
    AlbumBuilders albumBuilders,
    SettingService settingService,
    AlbumService albumService,
    InteractiveService interactivity,
    FmSettingService fmSettingService)
    : ComponentInteractionModule<ComponentInteractionContext>
{
    [ComponentInteraction(InteractionConstants.Album.Info)]
    [UsernameSetRequired]
    public async Task AlbumAsync(string album, string discordUser, string requesterDiscordUser)
    {
        await RespondAsync(InteractionCallback.DeferredModifyMessage);
        await this.Context.DisableInteractionButtons();

        var discordUserId = ulong.Parse(discordUser);
        var requesterDiscordUserId = ulong.Parse(requesterDiscordUser);
        var albumId = int.Parse(album);

        var contextUser = await userService.GetUserWithDiscogs(requesterDiscordUserId);
        var discordContextUser = await this.Context.GetUserAsync(requesterDiscordUserId);
        var userSettings = await settingService.GetOriginalContextUser(discordUserId, requesterDiscordUserId, this.Context.Guild, this.Context.User);

        var dbAlbum = await albumService.GetAlbumForId(albumId);

        try
        {
            var response = await albumBuilders.AlbumAsync(
                new ContextModel(this.Context, contextUser, discordContextUser), $"{dbAlbum.ArtistName} | {dbAlbum.Name}", userSettings);

            await this.Context.UpdateInteractionEmbed(response, interactivity, false);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [ComponentInteraction(InteractionConstants.WhoKnowsAlbumRolePicker)]
    [UsernameSetRequired]
    [RequiresIndex]
    public async Task WhoKnowsFilteringAsync(string albumId)
    {
        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);

        var album = await albumService.GetAlbumForId(int.Parse(albumId));

        var entityMenuInteraction = (EntityMenuInteraction)this.Context.Interaction;
        var roleIds = entityMenuInteraction.Data.SelectedValues.ToList();

        try
        {
            var response = await albumBuilders.WhoKnowsAlbumAsync(new ContextModel(this.Context, contextUser), ResponseMode.Embed, $"{album.ArtistName} | {album.Name}", true, roleIds);

            await this.Context.UpdateInteractionEmbed(response);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [ComponentInteraction(InteractionConstants.Album.Cover)]
    [UsernameSetRequired]
    public async Task AlbumCoverAsync(string album, string discordUser, string requesterDiscordUser, string type, string fmFlag = null)
    {
        var isFmContext = fmFlag == "fm";

        bool ephemeral = false;
        if (isFmContext)
        {
            var clickingUser = await userService.GetUserSettingsAsync(this.Context.User);
            var fmSetting = await fmSettingService.GetOrCreateFmSetting(clickingUser.UserId);
            ephemeral = fmSetting.PrivateButtonResponse != false; // null/true → private
        }

        if (ephemeral)
        {
            await RespondAsync(InteractionCallback.DeferredMessage(MessageFlags.Ephemeral));
        }
        else
        {
            await RespondAsync(InteractionCallback.DeferredModifyMessage);
            if (this.Context.Interaction is ButtonInteraction buttonInteraction && isFmContext)
            {
                await this.Context.DisableButtonsAndMenus(buttonInteraction.Data.CustomId);
            }
        }

        var discordUserId = ulong.Parse(discordUser);
        var requesterDiscordUserId = ulong.Parse(requesterDiscordUser);
        var albumId = int.Parse(album);

        var contextUser = await userService.GetUserWithDiscogs(requesterDiscordUserId);
        var discordContextUser = await this.Context.GetUserAsync(requesterDiscordUserId);
        var userSettings = await settingService.GetOriginalContextUser(discordUserId, requesterDiscordUserId, this.Context.Guild, this.Context.User);

        var dbAlbum = await albumService.GetAlbumForId(albumId);

        try
        {
            var response = await albumBuilders.CoverAsync(new ContextModel(this.Context, contextUser, discordContextUser), userSettings, $"{dbAlbum.ArtistName} | {dbAlbum.Name}", type == "motion");

            if (isFmContext && ephemeral)
            {
                response.Components = null;
            }

            if (ephemeral)
            {
                await this.Context.SendFollowUpResponse(interactivity, response, userService, ephemeral: true);
            }
            else
            {
                await this.Context.UpdateInteractionEmbed(response, interactivity, false);
            }

            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [ComponentInteraction(InteractionConstants.Album.RandomCover)]
    [UsernameSetRequired]
    public async Task RandomAlbumCoverAsync(string discordUser, string requesterDiscordUser)
    {
        var discordUserId = ulong.Parse(discordUser);
        var requesterDiscordUserId = ulong.Parse(requesterDiscordUser);

        if (this.Context.User.Id != requesterDiscordUserId)
        {
            await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
                .WithContent("🎲 Sorry, only the user that requested the random cover can reroll.")
                .WithFlags(MessageFlags.Ephemeral)));
            return;
        }

        await RespondAsync(InteractionCallback.DeferredModifyMessage);
        await this.Context.DisableInteractionButtons();

        var contextUser = await userService.GetUserWithDiscogs(requesterDiscordUserId);
        var discordContextUser = await this.Context.GetUserAsync(requesterDiscordUserId);
        var userSettings = await settingService.GetOriginalContextUser(discordUserId, requesterDiscordUserId, this.Context.Guild, this.Context.User);

        try
        {
            var response = await albumBuilders.CoverAsync(new ContextModel(this.Context, contextUser, discordContextUser), userSettings, "random");

            await this.Context.UpdateInteractionEmbed(response, interactivity, false);
            await this.Context.LogCommandUsedAsync(response, userService);

            var message = (this.Context.Interaction as MessageComponentInteraction)?.Message;
            if (message != null && response.ReferencedMusic != null && PublicProperties.UsedCommandsResponseContextId.TryGetValue(message.Id, out var contextId))
            {
                await userService.UpdateInteractionContext(contextId, response.ReferencedMusic);
            }
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [ComponentInteraction(InteractionConstants.Album.Tracks)]
    [UsernameSetRequired]
    public async Task AlbumTracksAsync(string album, string discordUser, string requesterDiscordUser, string fmFlag = null)
    {
        var isFmContext = fmFlag == "fm";

        bool ephemeral = false;
        if (isFmContext)
        {
            var clickingUser = await userService.GetUserSettingsAsync(this.Context.User);
            var fmSetting = await fmSettingService.GetOrCreateFmSetting(clickingUser.UserId);
            ephemeral = fmSetting.PrivateButtonResponse != false; // null/true → private
        }

        if (ephemeral)
        {
            await RespondAsync(InteractionCallback.DeferredMessage(MessageFlags.Ephemeral));
        }
        else
        {
            await RespondAsync(InteractionCallback.DeferredModifyMessage);
            await this.Context.DisableInteractionButtons();
        }

        var discordUserId = ulong.Parse(discordUser);
        var requesterDiscordUserId = ulong.Parse(requesterDiscordUser);
        var albumId = int.Parse(album);

        var contextUser = await userService.GetUserWithDiscogs(requesterDiscordUserId);
        var discordContextUser = await this.Context.GetUserAsync(requesterDiscordUserId);
        var userSettings = await settingService.GetOriginalContextUser(discordUserId, requesterDiscordUserId, this.Context.Guild, this.Context.User);

        var dbAlbum = await albumService.GetAlbumForId(albumId);

        try
        {
            var response = await albumBuilders.AlbumTracksAsync(
                new ContextModel(this.Context, contextUser, discordContextUser), userSettings, $"{dbAlbum.ArtistName} | {dbAlbum.Name}");

            if (isFmContext && ephemeral)
            {
                response.Components = null;
            }

            if (ephemeral)
            {
                await this.Context.SendFollowUpResponse(interactivity, response, userService, ephemeral: true);
            }
            else
            {
                await this.Context.UpdateInteractionEmbed(response, interactivity, false);
            }

            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }
}
