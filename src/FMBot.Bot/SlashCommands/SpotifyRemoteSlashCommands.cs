using System;
using System.Threading.Tasks;
using Fergun.Interactive;
using FMBot.Bot.Attributes;
using FMBot.Bot.Builders;
using FMBot.Bot.Extensions;
using FMBot.Bot.Models;
using FMBot.Bot.Services;
using FMBot.Bot.Services.ThirdParty;
using FMBot.Domain.Models;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;

namespace FMBot.Bot.SlashCommands;

public class SpotifyRemoteSlashCommands(
    InteractiveService interactivity,
    TrackService trackService,
    UserService userService,
    SpotifyRemoteService spotifyRemoteService,
    SpotifyRemoteBuilders spotifyRemoteBuilders)
    : ApplicationCommandModule<ApplicationCommandContext>
{
    private InteractiveService Interactivity { get; } = interactivity;

    [SlashCommand("remote", "Control your own Spotify playback",
        Contexts =
            [InteractionContextType.BotDMChannel, InteractionContextType.DMChannel, InteractionContextType.Guild],
        IntegrationTypes = [ApplicationIntegrationType.UserInstall, ApplicationIntegrationType.GuildInstall])]
    [UsernameSetRequired]
    [SpotifyConnectedRequired]
    public async Task RemoteAsync()
    {
        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);
        await RespondAsync(InteractionCallback.DeferredMessage(MessageFlags.Ephemeral));

        try
        {
            var response = await spotifyRemoteBuilders.BuildPanelAsync(new ContextModel(this.Context, contextUser));
            await this.Context.SendFollowUpResponse(this.Interactivity, response, userService, ephemeral: true);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [SlashCommand("queue", "Queue a track on your Spotify",
        Contexts =
            [InteractionContextType.BotDMChannel, InteractionContextType.DMChannel, InteractionContextType.Guild],
        IntegrationTypes = [ApplicationIntegrationType.UserInstall, ApplicationIntegrationType.GuildInstall])]
    [UsernameSetRequired]
    [SpotifyConnectedRequired]
    public async Task QueueAsync(
        [SlashCommandParameter(Name = "search", Description = "Track to queue (defaults to what you're playing)")]
        string searchValue = null)
    {
        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);
        await RespondAsync(InteractionCallback.DeferredMessage(MessageFlags.Ephemeral));

        try
        {
            var token = await spotifyRemoteService.GetActiveTokenAsync(this.Context.User.Id);
            if (token == null)
            {
                await SendFollowUp(SpotifyRemoteBuilders.NotConnectedResponse());
                return;
            }

            var track = await ResolveTrackAsync(contextUser.UserNameLastFM, contextUser.SessionKeyLastFm,
                contextUser.UserId, searchValue);
            if (track == null)
            {
                await SendFollowUp(SpotifyRemoteBuilders.TrackNotFoundResponse());
                return;
            }

            var result = await spotifyRemoteService.QueueAsync(token, track.Uri);
            await SendFollowUp(SpotifyRemoteBuilders.QueueResult(result, track));
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [SlashCommand("skip", "Skip to the next track on your Spotify",
        Contexts =
            [InteractionContextType.BotDMChannel, InteractionContextType.DMChannel, InteractionContextType.Guild],
        IntegrationTypes = [ApplicationIntegrationType.UserInstall, ApplicationIntegrationType.GuildInstall])]
    [UsernameSetRequired]
    [SpotifyConnectedRequired]
    public async Task SkipAsync()
    {
        await RespondAsync(InteractionCallback.DeferredMessage(MessageFlags.Ephemeral));

        try
        {
            var token = await spotifyRemoteService.GetActiveTokenAsync(this.Context.User.Id);
            if (token == null)
            {
                await SendFollowUp(SpotifyRemoteBuilders.NotConnectedResponse());
                return;
            }

            var result = await spotifyRemoteService.SkipAsync(token);
            await SendFollowUp(SpotifyRemoteBuilders.SkipResult(result));
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [SlashCommand("like", "Add a track to your Spotify liked songs",
        Contexts =
            [InteractionContextType.BotDMChannel, InteractionContextType.DMChannel, InteractionContextType.Guild],
        IntegrationTypes = [ApplicationIntegrationType.UserInstall, ApplicationIntegrationType.GuildInstall])]
    [UsernameSetRequired]
    [SpotifyConnectedRequired]
    public async Task LikeAsync(
        [SlashCommandParameter(Name = "search", Description = "Track to like (defaults to what you're playing)")]
        string searchValue = null)
    {
        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);
        await RespondAsync(InteractionCallback.DeferredMessage(MessageFlags.Ephemeral));

        try
        {
            var token = await spotifyRemoteService.GetActiveTokenAsync(this.Context.User.Id);
            if (token == null)
            {
                await SendFollowUp(SpotifyRemoteBuilders.NotConnectedResponse());
                return;
            }

            var track = await ResolveTrackAsync(contextUser.UserNameLastFM, contextUser.SessionKeyLastFm,
                contextUser.UserId, searchValue);
            if (track == null)
            {
                await SendFollowUp(SpotifyRemoteBuilders.TrackNotFoundResponse());
                return;
            }

            var wasInLibrary = await spotifyRemoteService.IsLikedAsync(token, track.Id);
            var result = await spotifyRemoteService.LikeAsync(token, track.Id);
            await SendFollowUp(SpotifyRemoteBuilders.LikeResult(result, track, false, wasInLibrary));
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [MessageCommand("Queue on Spotify",
        Contexts =
            [InteractionContextType.BotDMChannel, InteractionContextType.DMChannel, InteractionContextType.Guild],
        IntegrationTypes = [ApplicationIntegrationType.UserInstall, ApplicationIntegrationType.GuildInstall])]
    [UsernameSetRequired]
    [SpotifyConnectedRequired]
    public async Task QueueMessageAsync(RestMessage message)
    {
        await RemoteFromMessageAsync(message, play: false);
    }

    [MessageCommand("Play on Spotify",
        Contexts =
            [InteractionContextType.BotDMChannel, InteractionContextType.DMChannel, InteractionContextType.Guild],
        IntegrationTypes = [ApplicationIntegrationType.UserInstall, ApplicationIntegrationType.GuildInstall])]
    [UsernameSetRequired]
    [SpotifyConnectedRequired]
    public async Task PlayMessageAsync(RestMessage message)
    {
        await RemoteFromMessageAsync(message, play: true);
    }

    private async Task RemoteFromMessageAsync(RestMessage message, bool play)
    {
        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);
        await RespondAsync(InteractionCallback.DeferredMessage(MessageFlags.Ephemeral));

        try
        {
            var token = await spotifyRemoteService.GetActiveTokenAsync(this.Context.User.Id);
            if (token == null)
            {
                await SendFollowUp(SpotifyRemoteBuilders.NotConnectedResponse());
                return;
            }

            var track = await ResolveTrackFromMessageAsync(contextUser.UserNameLastFM, contextUser.SessionKeyLastFm,
                contextUser.UserId, message);
            if (track == null)
            {
                await SendFollowUp(SpotifyRemoteBuilders.TrackNotFoundResponse());
                return;
            }

            if (play)
            {
                var result = await spotifyRemoteService.PlayTrackAsync(token, track.Uri);
                await SendFollowUp(SpotifyRemoteBuilders.PlayResult(result, track));
            }
            else
            {
                var result = await spotifyRemoteService.QueueAsync(token, track.Uri);
                await SendFollowUp(SpotifyRemoteBuilders.QueueResult(result, track));
            }
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    private async Task<RemoteTrack> ResolveTrackAsync(string userNameLastFm, string sessionKey, int userId,
        string searchValue)
    {
        var trackSearch = await trackService.SearchTrack(new ResponseModel(), this.Context.User, searchValue,
            userNameLastFm, sessionKey, userId: userId, useCachedTracks: true);

        if (trackSearch.Track == null)
        {
            return null;
        }

        return await spotifyRemoteService.ResolveSpotifyTrackAsync(trackSearch.Track.ArtistName,
            trackSearch.Track.TrackName);
    }

    private async Task<RemoteTrack> ResolveTrackFromMessageAsync(string userNameLastFm, string sessionKey, int userId,
        RestMessage message)
    {
        if (message != null &&
            MusicLinkExtensions.TryParseMusicLink(message.Content) is
                { Type: MusicLinkExtensions.MusicLinkType.SpotifyTrack } spotifyLink)
        {
            var linkedTrack = await spotifyRemoteService.ResolveTrackByIdAsync(spotifyLink.Id);
            if (linkedTrack != null)
            {
                return linkedTrack;
            }
        }

        var trackSearch = await trackService.SearchTrack(new ResponseModel(), this.Context.User, null,
            userNameLastFm, sessionKey, userId: userId, useCachedTracks: true, referencedMessage: message);

        if (trackSearch.Track == null)
        {
            return null;
        }

        return await spotifyRemoteService.ResolveSpotifyTrackAsync(trackSearch.Track.ArtistName,
            trackSearch.Track.TrackName);
    }

    private async Task SendFollowUp(ResponseModel response)
    {
        await this.Context.SendFollowUpResponse(this.Interactivity, response, userService, ephemeral: true);
        await this.Context.LogCommandUsedAsync(response, userService);
    }
}
