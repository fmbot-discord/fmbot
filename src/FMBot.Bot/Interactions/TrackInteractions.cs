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
    InteractiveService interactivity)
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
                ResponseMode.Embed, $"{track.ArtistName} | {track.Name}", true, roleIds);

            await this.Context.UpdateInteractionEmbed(response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [ComponentInteraction(InteractionConstants.TrackPreview)]
    [UsernameSetRequired]
    public async Task TrackPreviewAsync(string trackId)
    {
        await RespondAsync(InteractionCallback.DeferredModifyMessage);

        await this.Context.DisableInteractionButtons();

        var parsedTrackId = int.Parse(trackId);
        var dbTrack = await trackService.GetTrackForId(parsedTrackId);

        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);

        var linkButton = new LinkButtonProperties(
            "https://open.spotify.com/track/" + dbTrack.SpotifyId,
            "Open on Spotify",
            EmojiProperties.Custom(DiscordConstants.Spotify));

        await this.Context.AddLinkButton(linkButton);

        try
        {
            var response = await trackBuilders.TrackPreviewAsync(new ContextModel(this.Context, contextUser),
                $"{dbTrack.ArtistName} | {dbTrack.Name}", Context.Interaction.Token);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [ComponentInteraction(InteractionConstants.TrackLyrics)]
    [UsernameSetRequired]
    public async Task TrackLyricsAsync(string trackId)
    {
        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);
        var context = new ContextModel(this.Context, contextUser);
        var supporterRequiredResponse = TrackBuilders.LyricsSupporterRequired(context);

        if (supporterRequiredResponse != null)
        {
            await this.Context.SendResponse(interactivity, supporterRequiredResponse, true);
            this.Context.LogCommandUsed(supporterRequiredResponse.CommandResponse);
            return;
        }

        await RespondAsync(InteractionCallback.DeferredModifyMessage);

        await this.Context.DisableInteractionButtons(specificButtonOnly: $"{InteractionConstants.TrackLyrics}:{trackId}");

        var parsedTrackId = int.Parse(trackId);
        var dbTrack = await trackService.GetTrackForId(parsedTrackId);

        try
        {
            var response =
                await trackBuilders.TrackLyricsAsync(context, $"{dbTrack.ArtistName} | {dbTrack.Name}");
            await this.Context.SendFollowUpResponse(interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }
}
