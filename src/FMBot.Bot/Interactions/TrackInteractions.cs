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
using FMBot.Domain.Models;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ComponentInteractions;

namespace FMBot.Bot.Interactions;

public class TrackInteractions(
    UserService userService,
    TrackBuilders trackBuilders,
    TrackService trackService,
    InteractiveService interactivity,
    FmSettingService fmSettingService)
    : ComponentInteractionModule<ComponentInteractionContext>
{
    [ComponentInteraction(InteractionConstants.WhoKnowsTrackRolePicker)]
    [UsernameSetRequired]
    [RequiresIndex]
    public async Task WhoKnowsFilteringAsync(string trackId)
    {
        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);

        var track = await trackService.GetTrackForId(int.Parse(trackId));

        var entityMenuInteraction = (EntityMenuInteraction)this.Context.Interaction;
        var roleIds = entityMenuInteraction.Data.SelectedValues.ToList();

        try
        {
            var response = await trackBuilders.WhoKnowsTrackAsync(new ContextModel(this.Context, contextUser),
                WhoKnowsResponseMode.Default, $"{track.ArtistName} | {track.Name}", true, roleIds);

            await this.Context.UpdateInteractionEmbed(response);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [ComponentInteraction(InteractionConstants.TrackPreview)]
    [UsernameSetRequired]
    public async Task TrackPreviewAsync(string trackId, string fmFlag = null)
    {
        var isFmContext = fmFlag == "fm";

        var ephemeral = false;
        if (isFmContext)
        {
            var clickingUser = await userService.GetUserSettingsAsync(this.Context.User);
            var fmSetting = await fmSettingService.GetOrCreateFmSetting(clickingUser.UserId);
            ephemeral = fmSetting.PrivateButtonResponse == true; // default public
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

        var parsedTrackId = int.Parse(trackId);
        var dbTrack = await trackService.GetTrackForId(parsedTrackId);

        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);

        if (!ephemeral)
        {
            var useSpotify = !string.IsNullOrEmpty(dbTrack.SpotifyPreviewUrl);

            var linkButton = useSpotify
                ? new LinkButtonProperties(
                    "https://open.spotify.com/track/" + dbTrack.SpotifyId,
                    "Open on Spotify",
                    EmojiProperties.Custom(DiscordConstants.Spotify))
                : new LinkButtonProperties(
                    dbTrack.AppleMusicUrl,
                    "Open on Apple Music",
                    EmojiProperties.Custom(DiscordConstants.AppleMusic));

            if (!isFmContext)
            {
                await this.Context.AddLinkButton(linkButton);
            }
        }

        try
        {
            var response = await trackBuilders.TrackPreviewAsync(new ContextModel(this.Context, contextUser),
                $"{dbTrack.ArtistName} | {dbTrack.Name}", Context.Interaction.Token);

            if (isFmContext && ephemeral)
            {
                response.Components = null;
            }

            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [ComponentInteraction(InteractionConstants.TrackLyrics)]
    [UsernameSetRequired]
    public async Task TrackLyricsAsync(string trackId, string fmFlag = null)
    {
        var isFmContext = fmFlag == "fm";
        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);
        var context = new ContextModel(this.Context, contextUser);
        var supporterRequiredResponse = TrackBuilders.LyricsSupporterRequired(context);

        if (supporterRequiredResponse != null)
        {
            await this.Context.SendResponse(interactivity, supporterRequiredResponse, userService, true);
            await this.Context.LogCommandUsedAsync(supporterRequiredResponse, userService);
            return;
        }

        var ephemeral = false;
        if (isFmContext)
        {
            var fmSetting = await fmSettingService.GetOrCreateFmSetting(contextUser.UserId);
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

        var parsedTrackId = int.Parse(trackId);
        var dbTrack = await trackService.GetTrackForId(parsedTrackId);

        try
        {
            var response =
                await trackBuilders.TrackLyricsAsync(context, $"{dbTrack.ArtistName} | {dbTrack.Name}");
            await this.Context.SendFollowUpResponse(interactivity, response, userService, ephemeral: ephemeral);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [ComponentInteraction(InteractionConstants.FmTrackDetails)]
    [UsernameSetRequired]
    public async Task FmTrackDetailsAsync(string trackId)
    {
        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);
        var fmSetting = await fmSettingService.GetOrCreateFmSetting(contextUser.UserId);
        var ephemeral = fmSetting.PrivateButtonResponse == true;

        if (ephemeral)
        {
            await RespondAsync(InteractionCallback.DeferredMessage(MessageFlags.Ephemeral));
        }
        else
        {
            await RespondAsync(InteractionCallback.DeferredModifyMessage);
            if (this.Context.Interaction is ButtonInteraction buttonInteraction)
            {
                await this.Context.DisableButtonsAndMenus(buttonInteraction.Data.CustomId);
            }
        }

        var dbTrack = await trackService.GetTrackForId(int.Parse(trackId));
        var context = new ContextModel(this.Context, contextUser);

        try
        {
            var response = await trackBuilders.TrackDetails(context,
                $"{dbTrack.ArtistName} | {dbTrack.Name}");

            if (ephemeral)
            {
                response.Components = null;
            }

            await this.Context.SendFollowUpResponse(interactivity, response, userService, ephemeral);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [ComponentInteraction(InteractionConstants.FmTrackLove)]
    [UsernameSetRequired]
    [UserSessionRequired]
    public async Task FmTrackLoveAsync(string trackId)
    {
        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);

        await RespondAsync(InteractionCallback.DeferredMessage(MessageFlags.Ephemeral));

        var dbTrack = await trackService.GetTrackForId(int.Parse(trackId));
        var context = new ContextModel(this.Context, contextUser);

        try
        {
            var response = await trackBuilders.LoveTrackAsync(context,
                $"{dbTrack.ArtistName} | {dbTrack.Name}");
            await this.Context.SendFollowUpResponse(interactivity, response, userService, ephemeral: true);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [ComponentInteraction(InteractionConstants.FmTrackUnlove)]
    [UsernameSetRequired]
    [UserSessionRequired]
    public async Task FmTrackUnloveAsync(string trackId)
    {
        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);

        await RespondAsync(InteractionCallback.DeferredMessage(MessageFlags.Ephemeral));

        var dbTrack = await trackService.GetTrackForId(int.Parse(trackId));
        var context = new ContextModel(this.Context, contextUser);

        try
        {
            var response = await trackBuilders.UnLoveTrackAsync(context,
                $"{dbTrack.ArtistName} | {dbTrack.Name}");
            await this.Context.SendFollowUpResponse(interactivity, response, userService, ephemeral: true);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }
}
