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
using FMBot.Domain.Enums;
using FMBot.Domain.Extensions;
using FMBot.Domain.Models;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ComponentInteractions;

namespace FMBot.Bot.Interactions;

public class ArtistInteractions(
    UserService userService,
    ArtistBuilders artistBuilders,
    SettingService settingService,
    ArtistsService artistsService,
    InteractiveService interactivity,
    FmSettingService fmSettingService)
    : ComponentInteractionModule<ComponentInteractionContext>
{
    [ComponentInteraction(InteractionConstants.Artist.Info)]
    [UsernameSetRequired]
    public async Task ArtistInfoAsync(string artistId, string discordUser, string requesterDiscordUser)
    {
        await RespondAsync(InteractionCallback.DeferredModifyMessage);
        await this.Context.DisableInteractionButtons();

        var discordUserId = ulong.Parse(discordUser);
        var requesterDiscordUserId = ulong.Parse(requesterDiscordUser);

        var contextUser = await userService.GetUserWithDiscogs(requesterDiscordUserId);
        var discordContextUser = await this.Context.GetUserAsync(requesterDiscordUserId);
        var userSettings = await settingService.GetOriginalContextUser(discordUserId, requesterDiscordUserId, this.Context.Guild, this.Context.User);

        var artist = await artistsService.GetArtistForId(int.Parse(artistId));

        try
        {
            var response = await artistBuilders.ArtistInfoAsync(new ContextModel(this.Context, contextUser, discordContextUser), userSettings, artist.Name, false);

            await this.Context.UpdateInteractionEmbed(response, interactivity, false);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [ComponentInteraction(InteractionConstants.Artist.Overview)]
    [UsernameSetRequired]
    public async Task ArtistOverviewAsync(string artistId, string discordUser, string requesterDiscordUser)
    {
        await RespondAsync(InteractionCallback.DeferredModifyMessage);
        await this.Context.DisableInteractionButtons();

        var discordUserId = ulong.Parse(discordUser);
        var requesterDiscordUserId = ulong.Parse(requesterDiscordUser);

        var contextUser = await userService.GetUserWithDiscogs(requesterDiscordUserId);
        var discordContextUser = await this.Context.GetUserAsync(requesterDiscordUserId);
        var userSettings = await settingService.GetOriginalContextUser(discordUserId, requesterDiscordUserId, this.Context.Guild, this.Context.User);

        var artist = await artistsService.GetArtistForId(int.Parse(artistId));

        try
        {
            var response = await artistBuilders.ArtistOverviewAsync(new ContextModel(this.Context, contextUser, discordContextUser), userSettings,
                artist.Name, false);

            await this.Context.UpdateInteractionEmbed(response, interactivity, false);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [ComponentInteraction(InteractionConstants.Artist.Tracks)]
    public async Task ArtistTracksAsync(string artistId, string discordUser, string requesterDiscordUser, string fmFlag = null)
    {
        var isFmContext = fmFlag == "fm";

        var ephemeral = false;
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

        var contextUser = await userService.GetUserAsync(requesterDiscordUserId);
        var discordContextUser = await this.Context.GetUserAsync(requesterDiscordUserId);
        var userSettings = await settingService.GetOriginalContextUser(discordUserId, requesterDiscordUserId, this.Context.Guild, this.Context.User);

        var artist = await artistsService.GetArtistForId(int.Parse(artistId));
        var timeSettings = SettingService.GetTimePeriod(Enum.GetName(typeof(PlayTimePeriod), PlayTimePeriod.AllTime), TimePeriod.AllTime);

        var response = await artistBuilders.ArtistTracksAsync(new ContextModel(this.Context, contextUser, discordContextUser), timeSettings,
            userSettings, artist.Name, false);

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

    [ComponentInteraction(InteractionConstants.Artist.Albums)]
    public async Task ArtistAlbumsAsync(string artistId, string discordUser, string requesterDiscordUser)
    {
        await RespondAsync(InteractionCallback.DeferredModifyMessage);
        await this.Context.DisableInteractionButtons();

        var discordUserId = ulong.Parse(discordUser);
        var requesterDiscordUserId = ulong.Parse(requesterDiscordUser);

        var contextUser = await userService.GetUserAsync(requesterDiscordUserId);
        var discordContextUser = await this.Context.GetUserAsync(requesterDiscordUserId);
        var userSettings = await settingService.GetOriginalContextUser(discordUserId, requesterDiscordUserId, this.Context.Guild, this.Context.User);

        var artist = await artistsService.GetArtistForId(int.Parse(artistId));

        var response = await artistBuilders.ArtistAlbumsAsync(new ContextModel(this.Context, contextUser, discordContextUser),
            userSettings, artist.Name, false);

        await this.Context.UpdateInteractionEmbed(response, interactivity, false);
        await this.Context.LogCommandUsedAsync(response, userService);
    }

    [ComponentInteraction(InteractionConstants.WhoKnowsRolePicker)]
    [UsernameSetRequired]
    [RequiresIndex]
    public async Task WhoKnowsFilteringAsync(string artistId)
    {
        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);

        var artist = await artistsService.GetArtistForId(int.Parse(artistId));

        var entityMenuInteraction = (EntityMenuInteraction)this.Context.Interaction;
        var roleIds = entityMenuInteraction.Data.SelectedValues.ToList();

        try
        {
            // TODO add redirects
            var response = await artistBuilders.WhoKnowsArtistAsync(new ContextModel(this.Context, contextUser), ResponseMode.Embed, artist.Name, true, roleIds);

            await this.Context.UpdateInteractionEmbed(response);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [ComponentInteraction(InteractionConstants.Artist.WhoKnows)]
    [UsernameSetRequired]
    [RequiresIndex]
    public async Task WhoKnowsAsync(string artistId)
    {
        await RespondAsync(InteractionCallback.DeferredModifyMessage);
        await this.Context.DisableInteractionButtons();

        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);
        var artist = await artistsService.GetArtistForId(int.Parse(artistId));
        var mode = contextUser.WhoKnowsMode.ToResponseMode();

        try
        {
            var response = await artistBuilders.WhoKnowsArtistAsync(new ContextModel(this.Context, contextUser), mode, artist.Name, showCrownButton: true);

            await this.Context.UpdateInteractionEmbed(response, defer: false);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }
}
