using System;
using System.Threading.Tasks;
using Fergun.Interactive;
using FMBot.Bot.Attributes;
using FMBot.Bot.Builders;
using FMBot.Bot.Extensions;
using FMBot.Bot.Interfaces;
using FMBot.Bot.Models;
using FMBot.Bot.Services;
using FMBot.Bot.Services.ThirdParty;
using FMBot.Domain.Models;
using Microsoft.Extensions.Options;
using NetCord.Services.Commands;
using Shared.Domain.Models;

namespace FMBot.Bot.TextCommands;

[ModuleName("Spotify Remote")]
public class SpotifyRemoteCommands(
    IPrefixService prefixService,
    TrackService trackService,
    UserService userService,
    AdminService adminService,
    SpotifyRemoteService spotifyRemoteService,
    SpotifyRemoteBuilders spotifyRemoteBuilders,
    IOptions<BotSettings> botSettings,
    InteractiveService interactivity)
    : BaseCommandModule(botSettings)
{
    private InteractiveService Interactivity { get; } = interactivity;

    [Command("queue", "rq", "q")]
    [Summary("Queues a track on your Spotify")]
    [Examples("queue", "rq", "queue Tame Impala Borderline")]
    [UsernameSetRequired]
    [SpotifyConnectedRequired]
    [CommandCategories(CommandCategory.ThirdParty)]
    public async Task QueueAsync([CommandParameter(Remainder = true)] string searchValue = null)
    {
        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);

        try
        {
            _ = this.Context.Channel?.TriggerTypingAsync()!;

            var token = await spotifyRemoteService.GetActiveTokenAsync(this.Context.User.Id);
            if (token == null)
            {
                await SendNotConnected();
                return;
            }

            if (await TryHandleAlbumOrArtistAsync(token, searchValue, play: false))
            {
                return;
            }

            var track = await ResolveTrackAsync(contextUser.UserNameLastFM, contextUser.SessionKeyLastFm,
                contextUser.UserId, searchValue);
            if (track == null)
            {
                await SendResponse(SpotifyRemoteBuilders.TrackNotFoundResponse());
                return;
            }

            var result = await spotifyRemoteService.QueueAsync(token, track.Uri);
            await SendResponse(SpotifyRemoteBuilders.QueueResult(result, track));
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [Command("skip", "rs", "rcskip", "rcs")]
    [Summary("Skips to the next track on your Spotify")]
    [UsernameSetRequired]
    [SpotifyConnectedRequired]
    [CommandCategories(CommandCategory.ThirdParty)]
    public async Task SkipAsync()
    {
        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);

        try
        {
            _ = this.Context.Channel?.TriggerTypingAsync()!;

            var token = await spotifyRemoteService.GetActiveTokenAsync(this.Context.User.Id);
            if (token == null)
            {
                await SendNotConnected();
                return;
            }

            var result = await spotifyRemoteService.SkipAsync(token);
            await SendResponse(SpotifyRemoteBuilders.SkipResult(result));
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [Command("previous", "prev", "rcprev")]
    [Summary("Goes back to the previous track on your Spotify")]
    [UsernameSetRequired]
    [SpotifyConnectedRequired]
    [CommandCategories(CommandCategory.ThirdParty)]
    public async Task PreviousAsync()
    {
        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);

        try
        {
            _ = this.Context.Channel?.TriggerTypingAsync()!;

            var token = await spotifyRemoteService.GetActiveTokenAsync(this.Context.User.Id);
            if (token == null)
            {
                await SendNotConnected();
                return;
            }

            var result = await spotifyRemoteService.PreviousAsync(token);
            await SendResponse(SpotifyRemoteBuilders.PreviousResult(result));
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [Command("play", "resume", "rcplay")]
    [Summary("Plays a track on your Spotify, or resumes playback when no track is given")]
    [Examples("play", "play Tame Impala Borderline")]
    [UsernameSetRequired]
    [SpotifyConnectedRequired]
    [CommandCategories(CommandCategory.ThirdParty)]
    public async Task PlayAsync([CommandParameter(Remainder = true)] string searchValue = null)
    {
        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);

        try
        {
            _ = this.Context.Channel?.TriggerTypingAsync()!;

            var token = await spotifyRemoteService.GetActiveTokenAsync(this.Context.User.Id);
            if (token == null)
            {
                await SendNotConnected();
                return;
            }

            if (string.IsNullOrWhiteSpace(searchValue) && this.Context.Message.ReferencedMessage == null)
            {
                var resumeResult = await spotifyRemoteService.ResumeAsync(token);
                await SendResponse(SpotifyRemoteBuilders.PlayPauseResult(resumeResult, true));
                return;
            }

            if (await TryHandleAlbumOrArtistAsync(token, searchValue, play: true))
            {
                return;
            }

            var track = await ResolveTrackAsync(contextUser.UserNameLastFM, contextUser.SessionKeyLastFm,
                contextUser.UserId, searchValue);
            if (track == null)
            {
                await SendResponse(SpotifyRemoteBuilders.TrackNotFoundResponse());
                return;
            }

            var result = await spotifyRemoteService.PlayTrackAsync(token, track.Uri, track.AlbumUri);
            await SendResponse(SpotifyRemoteBuilders.PlayResult(result, track));
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [Command("pause", "rcpause")]
    [Summary("Pauses playback on your Spotify")]
    [UsernameSetRequired]
    [SpotifyConnectedRequired]
    [CommandCategories(CommandCategory.ThirdParty)]
    public async Task PauseAsync()
    {
        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);

        try
        {
            _ = this.Context.Channel?.TriggerTypingAsync()!;

            var token = await spotifyRemoteService.GetActiveTokenAsync(this.Context.User.Id);
            if (token == null)
            {
                await SendNotConnected();
                return;
            }

            var result = await spotifyRemoteService.PauseAsync(token);
            await SendResponse(SpotifyRemoteBuilders.PlayPauseResult(result, false));
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [Command("rl", "rclike", "rcl", "spotifylike", "rclove", "spotifylove")]
    [Summary("Adds a track to your Spotify liked songs")]
    [Examples("rl", "rl Tame Impala Borderline")]
    [UsernameSetRequired]
    [SpotifyConnectedRequired]
    [CommandCategories(CommandCategory.ThirdParty)]
    public async Task LikeAsync([CommandParameter(Remainder = true)] string searchValue = null)
    {
        await LikeOrUnlike(searchValue, false);
    }

    [Command("rul", "rcunlike", "rcul", "spotifyunlike", "rcunlove", "spotifyunlove")]
    [Summary("Removes a track from your Spotify liked songs")]
    [Examples("rul", "rul Tame Impala Borderline")]
    [UsernameSetRequired]
    [SpotifyConnectedRequired]
    [CommandCategories(CommandCategory.ThirdParty)]
    public async Task UnlikeAsync([CommandParameter(Remainder = true)] string searchValue = null)
    {
        await LikeOrUnlike(searchValue, true);
    }

    [Command("remote", "rc", "spotifyconnect")]
    [Summary("Opens the Spotify remote, or use `remote disconnect` to unlink your Spotify. This is a remote only — it does not scrobble or track your listening.")]
    [Examples("remote", "remote disconnect")]
    [UsernameSetRequired]
    [SpotifyConnectedRequired]
    [CommandCategories(CommandCategory.ThirdParty)]
    public async Task RemoteAsync([CommandParameter(Remainder = true)] string action = null)
    {
        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);
        var prfx = prefixService.GetPrefix(this.Context.Guild?.Id);

        try
        {
            if (IsDisconnectAction(action))
            {
                await spotifyRemoteService.RemoveTokenAsync(this.Context.User.Id);
                await SendResponse(SpotifyRemoteBuilders.DisconnectedPanelResponse());
                return;
            }

            var response =
                await spotifyRemoteBuilders.BuildPanelAsync(new ContextModel(this.Context, prfx, contextUser));
            await SendResponse(response);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [Command("remotedemo", "replydemo", "rcdemo")]
    [Summary("Posts a Spotify remote reply-context demo")]
    [ExcludeFromHelp]
    [CommandCategories(CommandCategory.ThirdParty)]
    public async Task RemoteDemoAsync()
    {
        if (!await adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
        {
            await this.Context.LogCommandUsedAsync(
                new ResponseModel { CommandResponse = CommandResponse.NoPermission }, userService);
            return;
        }

        try
        {
            await SendResponse(SpotifyRemoteBuilders.ReplyContextDemoResponse());
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    private static bool IsDisconnectAction(string action)
    {
        if (string.IsNullOrWhiteSpace(action))
        {
            return false;
        }

        var trimmed = action.Trim();
        return string.Equals(trimmed, "remove", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(trimmed, "disconnect", StringComparison.OrdinalIgnoreCase);
    }

    private async Task LikeOrUnlike(string searchValue, bool unlike)
    {
        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);

        try
        {
            _ = this.Context.Channel?.TriggerTypingAsync()!;

            var token = await spotifyRemoteService.GetActiveTokenAsync(this.Context.User.Id);
            if (token == null)
            {
                await SendNotConnected();
                return;
            }

            var track = await ResolveTrackAsync(contextUser.UserNameLastFM, contextUser.SessionKeyLastFm,
                contextUser.UserId, searchValue);
            if (track == null)
            {
                await SendResponse(SpotifyRemoteBuilders.TrackNotFoundResponse());
                return;
            }

            var wasInLibrary = await spotifyRemoteService.IsLikedAsync(token, track.Id);
            var result = unlike
                ? await spotifyRemoteService.UnlikeAsync(token, track.Id)
                : await spotifyRemoteService.LikeAsync(token, track.Id);

            await SendResponse(SpotifyRemoteBuilders.LikeResult(result, track, unlike, wasInLibrary));
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    private async Task<bool> TryHandleAlbumOrArtistAsync(UserToken token, string searchValue, bool play)
    {
        var referencedMessage = this.Context.Message.ReferencedMessage;
        var content = !string.IsNullOrWhiteSpace(searchValue)
            ? searchValue
            : referencedMessage?.Content;

        var link = MusicLinkExtensions.TryParseMusicLink(content);

        ReferencedMusic referenced = null;
        if (link == null && string.IsNullOrWhiteSpace(searchValue) && referencedMessage != null)
        {
            referenced = CommandContextExtensions.GetReferencedMusic(referencedMessage.Id)
                         ?? await userService.GetReferencedMusic(referencedMessage.Id);
        }

        var response = await spotifyRemoteBuilders.TryQueueOrPlayAlbumOrArtistAsync(token, link, referenced, play);
        if (response == null)
        {
            return false;
        }

        await SendResponse(response);
        return true;
    }

    private async Task<RemoteTrack> ResolveTrackAsync(string userNameLastFm, string sessionKey, int userId,
        string searchValue)
    {
        var referencedMessage = this.Context.Message.ReferencedMessage;

        if (string.IsNullOrWhiteSpace(searchValue) && referencedMessage != null &&
            MusicLinkExtensions.TryParseMusicLink(referencedMessage.Content) is
                { Type: MusicLinkExtensions.MusicLinkType.SpotifyTrack } spotifyLink)
        {
            var linkedTrack = await spotifyRemoteService.ResolveTrackByIdAsync(spotifyLink.Id);
            if (linkedTrack != null)
            {
                return linkedTrack;
            }
        }

        var trackSearch = await trackService.SearchTrack(new ResponseModel(), this.Context.User, searchValue,
            userNameLastFm, sessionKey, userId: userId, useCachedTracks: true, referencedMessage: referencedMessage);

        if (trackSearch.Track == null)
        {
            return null;
        }

        return await spotifyRemoteService.ResolveSpotifyTrackAsync(trackSearch.Track.ArtistName,
            trackSearch.Track.TrackName);
    }

    private async Task SendNotConnected()
    {
        await SendResponse(SpotifyRemoteBuilders.NotConnectedResponse());
    }

    private async Task SendResponse(ResponseModel response)
    {
        await this.Context.SendResponse(this.Interactivity, response, userService);
        await this.Context.LogCommandUsedAsync(response, userService);
    }
}
