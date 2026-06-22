using System;
using System.Linq;
using System.Threading.Tasks;
using FMBot.Bot.Attributes;
using FMBot.Bot.Builders;
using FMBot.Bot.Extensions;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Bot.Services.ThirdParty;
using FMBot.Domain.Models;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ComponentInteractions;
using SpotifyAPI.Web;

namespace FMBot.Bot.Interactions;

public class SpotifyRemoteInteractions(
    UserService userService,
    SpotifyRemoteService spotifyRemoteService,
    SpotifyRemoteBuilders spotifyRemoteBuilders)
    : ComponentInteractionModule<ComponentInteractionContext>
{
    [ComponentInteraction(InteractionConstants.Remote.Connect)]
    [UsernameSetRequired]
    public async Task ConnectButton()
    {
        try
        {
            if (!spotifyRemoteService.IsConfigured)
            {
                await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
                    .WithContent("The Spotify remote isn't available right now. Please try again later.")
                    .WithFlags(MessageFlags.Ephemeral)));
                return;
            }

            var authUrl = spotifyRemoteService.BuildAuthUrl(this.Context.User.Id);
            var linkResponse = SpotifyRemoteBuilders.ConnectLinkResponse(authUrl);

            await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
                .WithComponents(linkResponse.GetComponentsV2())
                .WithFlags(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2)));
            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.Ok },
                userService);

            var connected = await spotifyRemoteService.WaitForConnectionAsync(this.Context.User.Id);
            var resultResponse = connected
                ? SpotifyRemoteBuilders.ConnectSuccessResponse()
                : SpotifyRemoteBuilders.ConnectTimeoutResponse();

            await this.Context.Interaction.ModifyResponseAsync(m =>
            {
                m.Components = resultResponse.GetComponentsV2();
            });
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService, deferFirst: false);
        }
    }

    [ComponentInteraction(InteractionConstants.Remote.Skip)]
    [UsernameSetRequired]
    [SpotifyConnectedRequired]
    public async Task SkipButton(string ownerId)
    {
        if (!await EnsureOwner(ownerId))
        {
            return;
        }

        try
        {
            await RespondAsync(InteractionCallback.DeferredModifyMessage);

            var token = await spotifyRemoteService.GetActiveTokenAsync(this.Context.User.Id);
            if (token == null)
            {
                await SendEphemeral(SpotifyRemoteBuilders.NotConnectedResponse());
                return;
            }

            var result = await spotifyRemoteService.SkipAsync(token);
            if (result == RemoteActionResult.Ok)
            {
                await RefreshPanel();
            }
            else
            {
                await SendEphemeral(SpotifyRemoteBuilders.SkipResult(result));
            }
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [ComponentInteraction(InteractionConstants.Remote.Previous)]
    [UsernameSetRequired]
    [SpotifyConnectedRequired]
    public async Task PreviousButton(string ownerId)
    {
        if (!await EnsureOwner(ownerId))
        {
            return;
        }

        try
        {
            await RespondAsync(InteractionCallback.DeferredModifyMessage);

            var token = await spotifyRemoteService.GetActiveTokenAsync(this.Context.User.Id);
            if (token == null)
            {
                await SendEphemeral(SpotifyRemoteBuilders.NotConnectedResponse());
                return;
            }

            var result = await spotifyRemoteService.PreviousAsync(token);
            if (result == RemoteActionResult.Ok)
            {
                await RefreshPanel();
            }
            else
            {
                await SendEphemeral(SpotifyRemoteBuilders.PreviousResult(result));
            }
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [ComponentInteraction(InteractionConstants.Remote.PlayPause)]
    [UsernameSetRequired]
    [SpotifyConnectedRequired]
    public async Task PlayPauseButton(string ownerId)
    {
        if (!await EnsureOwner(ownerId))
        {
            return;
        }

        try
        {
            await RespondAsync(InteractionCallback.DeferredModifyMessage);

            var token = await spotifyRemoteService.GetActiveTokenAsync(this.Context.User.Id);
            if (token == null)
            {
                await SendEphemeral(SpotifyRemoteBuilders.NotConnectedResponse());
                return;
            }

            var playback = await spotifyRemoteService.GetPlaybackAsync(token);
            var resume = !(playback?.IsPlaying ?? false);

            var result = resume
                ? await spotifyRemoteService.ResumeAsync(token)
                : await spotifyRemoteService.PauseAsync(token);

            if (result == RemoteActionResult.Ok)
            {
                await RefreshPanel();
            }
            else
            {
                await SendEphemeral(SpotifyRemoteBuilders.PlayPauseResult(result, resume));
            }
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [ComponentInteraction(InteractionConstants.Remote.Like)]
    [UsernameSetRequired]
    [SpotifyConnectedRequired]
    public async Task LikeButton(string ownerId)
    {
        if (!await EnsureOwner(ownerId))
        {
            return;
        }

        try
        {
            await RespondAsync(InteractionCallback.DeferredModifyMessage);

            var token = await spotifyRemoteService.GetActiveTokenAsync(this.Context.User.Id);
            if (token == null)
            {
                await SendEphemeral(SpotifyRemoteBuilders.NotConnectedResponse());
                return;
            }

            var playback = await spotifyRemoteService.GetPlaybackAsync(token);
            if (playback?.Item is not FullTrack fullTrack)
            {
                await SendEphemeral(SpotifyRemoteBuilders.TrackNotFoundResponse());
                return;
            }

            var wasInLibrary = await spotifyRemoteService.IsLikedAsync(token, fullTrack.Id);
            var result = wasInLibrary
                ? await spotifyRemoteService.UnlikeAsync(token, fullTrack.Id)
                : await spotifyRemoteService.LikeAsync(token, fullTrack.Id);

            if (result == RemoteActionResult.Ok)
            {
                await RefreshPanel();
            }
            else
            {
                await SendEphemeral(SpotifyRemoteBuilders.LikeResult(result, RemoteTrack.From(fullTrack), wasInLibrary,
                    wasInLibrary));
            }
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [ComponentInteraction(InteractionConstants.Remote.Panel)]
    [UsernameSetRequired]
    [SpotifyConnectedRequired]
    public async Task RefreshButton(string ownerId)
    {
        if (!await EnsureOwner(ownerId))
        {
            return;
        }

        try
        {
            await RespondAsync(InteractionCallback.DeferredModifyMessage);
            await RefreshPanel();
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [ComponentInteraction(InteractionConstants.Remote.Disconnect)]
    [UsernameSetRequired]
    public async Task DisconnectButton(string ownerId)
    {
        if (!await EnsureOwner(ownerId))
        {
            return;
        }

        try
        {
            await RespondAsync(InteractionCallback.DeferredModifyMessage);

            await spotifyRemoteService.RemoveTokenAsync(this.Context.User.Id);

            var response = SpotifyRemoteBuilders.DisconnectedPanelResponse();
            await this.Context.Interaction.ModifyResponseAsync(m =>
            {
                m.Components = response.GetComponentsV2();
                m.AllowedMentions = AllowedMentionsProperties.None;
            });
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    private async Task<bool> EnsureOwner(string ownerId)
    {
        if (this.Context.User.Id.ToString() == ownerId)
        {
            return true;
        }

        await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
            .WithContent("This isn't your remote. Run `.rc` to open your own.")
            .WithFlags(MessageFlags.Ephemeral)));
        return false;
    }

    private async Task RefreshPanel()
    {
        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);
        var panel = await spotifyRemoteBuilders.BuildPanelAsync(new ContextModel(this.Context, contextUser));

        await this.Context.Interaction.ModifyResponseAsync(m =>
        {
            m.Components = panel.GetComponentsV2();
            m.AllowedMentions = AllowedMentionsProperties.None;
        });
    }

    private async Task SendEphemeral(ResponseModel response)
    {
        await this.Context.Interaction.SendFollowupMessageAsync(new InteractionMessageProperties()
            .WithComponents(response.GetComponentsV2())
            .WithFlags(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2));
    }
}
