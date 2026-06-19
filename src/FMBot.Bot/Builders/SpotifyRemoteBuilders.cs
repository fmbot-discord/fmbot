using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using FMBot.Bot.Extensions;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Bot.Services.ThirdParty;
using FMBot.Domain.Models;
using NetCord;
using NetCord.Rest;
using SpotifyAPI.Web;

namespace FMBot.Bot.Builders;

public class SpotifyRemoteBuilders(SpotifyRemoteService spotifyRemoteService)
{
    public static ResponseModel NotConnectedResponse()
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.ComponentsV2
        };

        var container = response.ComponentsContainer;
        container.WithAccentColor(DiscordConstants.SpotifyColorGreen);
        container.WithTextDisplay(
            $"{EmojiProperties.Custom(DiscordConstants.Spotify).ToDiscordString("spotify")} **Connect Spotify to use the .fmbot remote**\n\n" +
            "This lets you control your own Spotify playback, queue, skip, like and play/pause, straight from Discord. Your Spotify account remains private and will not be shown to other users.\n\n" +
            "**It's a remote only. It does not scrobble or track your listening** - that's handled by Last.fm, which .fmbot uses for your stats. Connecting won't change how your music is tracked.");
        container.WithSeparator();
        container.WithActionRow([
            new ButtonProperties(InteractionConstants.Remote.Connect, "Connect",
                EmojiProperties.Custom(DiscordConstants.Spotify), ButtonStyle.Success)
        ]);

        return response;
    }

    public static ResponseModel ConnectLinkResponse(string authUrl)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.ComponentsV2
        };

        var container = response.ComponentsContainer;
        container.WithAccentColor(DiscordConstants.SpotifyColorGreen);
        container.WithTextDisplay(
            $"{EmojiProperties.Custom(DiscordConstants.Spotify).ToDiscordString("spotify")} **Connecting Spotify to .fmbot..**\n\n" +
            "Click **Connect** and authorize .fmbot to control your Spotify playback.");
        container.WithSeparator();
        container.WithActionRow([
            new LinkButtonProperties(authUrl, "Connect", EmojiProperties.Custom(DiscordConstants.Spotify))
        ]);

        return response;
    }

    public static ResponseModel ConnectSuccessResponse()
    {
        return Cv2Message(DiscordConstants.SuccessColorGreen,
            "✅ **Spotify connected!**\n\n" +
            "You can now control your playback with `/remote`, `queue`, `skip` and `like`.\n\n" +
            "Tip: Reply to any Spotify link or .fmbot command with a track with `queue` to add it to your queue.");
    }

    public static ResponseModel ConnectTimeoutResponse()
    {
        var response = Cv2Message(DiscordConstants.WarningColorOrange,
            "❌ Spotify connection timed out.\n\n" +
            "Run the command again to retry connecting.");
        response.CommandResponse = CommandResponse.Cooldown;

        return response;
    }

    public async Task<ResponseModel> BuildPanelAsync(ContextModel context)
    {
        var token = await spotifyRemoteService.GetActiveTokenAsync(context.DiscordUser.Id);
        if (token == null)
        {
            return NotConnectedResponse();
        }

        var response = new ResponseModel
        {
            ResponseType = ResponseType.ComponentsV2
        };

        var container = response.ComponentsContainer;
        var playback = await spotifyRemoteService.GetPlaybackAsync(token);

        container.WithAccentColor(DiscordConstants.SpotifyColorGreen);

        var title = new StringBuilder();
        title.AppendLine(playback.IsPlaying
            ? $"Now playing"
            : $"Paused");
        container.WithTextDisplay($"{EmojiProperties.Custom(DiscordConstants.Spotify).ToDiscordString("spotify")} Spotify remote - {title}");

        container.WithSeparator();

        var repeatLabel = playback.RepeatState switch
        {
            "track" => "Playing track on repeat",
            _ => null
        };

        var isLiked = false;
        List<RemoteTrack> queue = [];

        if (playback?.Item is FullTrack fullTrack)
        {
            var current = RemoteTrack.From(fullTrack);
            isLiked = await spotifyRemoteService.IsLikedAsync(token, fullTrack.Id);

            if (repeatLabel == null)
            {
                queue = await spotifyRemoteService.GetQueueAsync(token);
            }

            var currentTrack = new RecentTrack
            {
                TrackName = current.Name,
                TrackUrl = $"https://open.spotify.com/track/{current.Id}",
                ArtistName = current.ArtistName,
                AlbumName = fullTrack.Album?.Name
            };

            container.WithTextDisplay(StringService.TrackToLinkedString(currentTrack).TrimEnd());

            response.ReferencedMusic = new ReferencedMusic
            {
                Artist = current.ArtistName,
                Album = fullTrack.Album?.Name,
                Track = current.Name
            };
        }
        else
        {
            container.WithTextDisplay("Nothing is playing on Spotify right now.\nStart a song, then hit <:refresh:1517501604167811093> to refresh.");
        }

        if (repeatLabel != null)
        {
            container.WithSeparator();
            container.WithTextDisplay($"-# Up next\n"+
            $"🔁 {repeatLabel}");
        }
        else if (queue.Count > 0)
        {
            var upNext = new StringBuilder();
            upNext.AppendLine("-# Up next");
            for (var i = 0; i < Math.Min(queue.Count, 4); i++)
            {
                var queued = queue[i];
                upNext.AppendLine(
                    $"{i + 1}. [{StringExtensions.Sanitize(queued.Name)}](https://open.spotify.com/track/{queued.Id}) by {StringExtensions.Sanitize(queued.ArtistName)}");
            }

            container.WithSeparator();
            container.WithTextDisplay(upNext.ToString().TrimEnd());
        }

        container.WithSeparator();

        var ownerId = context.DiscordUser.Id;
        var row = new ActionRowProperties
        {
            new ButtonProperties($"{InteractionConstants.Remote.Previous}:{ownerId}",
                EmojiProperties.Custom(DiscordConstants.PagesFirst), ButtonStyle.Secondary),
            new ButtonProperties($"{InteractionConstants.Remote.PlayPause}:{ownerId}",
                EmojiProperties.Custom(playback?.IsPlaying == true ? DiscordConstants.Pause : DiscordConstants.PagesNext), ButtonStyle.Secondary),
            new ButtonProperties($"{InteractionConstants.Remote.Skip}:{ownerId}",
                EmojiProperties.Custom(DiscordConstants.PagesLast), ButtonStyle.Secondary),
            new ButtonProperties($"{InteractionConstants.Remote.Like}:{ownerId}",
                EmojiProperties.Standard("❤️"), isLiked ? ButtonStyle.Success : ButtonStyle.Secondary),
            new ButtonProperties($"{InteractionConstants.Remote.Panel}:{ownerId}",
                EmojiProperties.Custom(DiscordConstants.Refresh), ButtonStyle.Secondary)
        };
        container.WithActionRow(row);

        // var manageRow = new ActionRowProperties
        // {
        //     new ButtonProperties($"{InteractionConstants.Remote.Disconnect}:{ownerId}", "Disconnect",
        //         ButtonStyle.Danger)
        // };
        // container.WithActionRow(manageRow);

        // container.WithSeparator();
        // container.WithTextDisplay(
        //     "-# 🎛️ .fmbot remote");

        return response;
    }

    public static ResponseModel DisconnectedPanelResponse()
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.ComponentsV2
        };

        var container = response.ComponentsContainer;
        container.WithAccentColor(DiscordConstants.SpotifyColorGreen);
        container.WithTextDisplay(
            $"{EmojiProperties.Custom(DiscordConstants.Spotify).ToDiscordString("spotify")} **Spotify remote disconnected**\n" +
            "Disconnected - your Spotify is no longer linked to the remote.");

        return response;
    }

    public static ResponseModel QueueResult(RemoteActionResult result, RemoteTrack track)
    {
        return result != RemoteActionResult.Ok
            ? ErrorResponse(result)
            : TrackResultMessage("Added to Spotify queue:", track);
    }

    public static ResponseModel PlayResult(RemoteActionResult result, RemoteTrack track)
    {
        return result != RemoteActionResult.Ok
            ? ErrorResponse(result)
            : TrackResultMessage(
                $"{EmojiProperties.Custom(DiscordConstants.PagesNext).ToDiscordString("play")} Started playing on Spotify:",
                track);
    }

    public static ResponseModel SkipResult(RemoteActionResult result)
    {
        return result != RemoteActionResult.Ok
            ? ErrorResponse(result)
            : SuccessMessage($"{EmojiProperties.Custom(DiscordConstants.PagesLast).ToDiscordString("next")} Skipped to the next track.");
    }

    public static ResponseModel PreviousResult(RemoteActionResult result)
    {
        return result != RemoteActionResult.Ok
            ? ErrorResponse(result)
            : SuccessMessage($"{EmojiProperties.Custom(DiscordConstants.PagesFirst).ToDiscordString("previous")} Went back to the previous track.");
    }

    public static ResponseModel PlayPauseResult(RemoteActionResult result, bool resumed)
    {
        if (result == RemoteActionResult.Restriction)
        {
            return SuccessMessage(resumed
                ? $"{EmojiProperties.Custom(DiscordConstants.PagesNext).ToDiscordString("play")} Already playing."
                : $"{EmojiProperties.Custom(DiscordConstants.Pause).ToDiscordString("pause")} Already paused.");
        }

        return result != RemoteActionResult.Ok
            ? ErrorResponse(result)
            : SuccessMessage(resumed
                ? $"{EmojiProperties.Custom(DiscordConstants.PagesNext).ToDiscordString("play")} Resumed playback."
                : $"{EmojiProperties.Custom(DiscordConstants.Pause).ToDiscordString("pause")} Paused playback.");
    }

    public static ResponseModel LikeResult(RemoteActionResult result, RemoteTrack track, bool unlike, bool wasInLibrary)
    {
        if (result != RemoteActionResult.Ok)
        {
            return ErrorResponse(result);
        }

        var label = (unlike, wasInLibrary) switch
        {
            (true, true) => "💔 Removed from your liked songs on Spotify:",
            (true, false) => "Wasn't in your liked songs on Spotify:",
            (false, true) => "❤️ Already in your liked songs on Spotify:",
            (false, false) => "❤️ Added to your liked songs on Spotify:"
        };

        return TrackResultMessage(label, track);
    }

    private static ResponseModel TrackResultMessage(string label, RemoteTrack track)
    {
        var recentTrack = new RecentTrack
        {
            TrackName = track.Name,
            TrackUrl = $"https://open.spotify.com/track/{track.Id}",
            ArtistName = track.ArtistName,
            AlbumName = track.AlbumName
        };

        var response = SuccessMessage(
            $"{label}\n{StringService.TrackToLinkedString(recentTrack).TrimEnd()}",
            track.AlbumImageUrl);

        response.ReferencedMusic = new ReferencedMusic
        {
            Artist = track.ArtistName,
            Album = track.AlbumName,
            Track = track.Name
        };

        return response;
    }

    public static ResponseModel TrackNotFoundResponse()
    {
        var response = Cv2Message(DiscordConstants.WarningColorOrange,
            "Couldn't find a track to use. Search for one (e.g. `queue daft punk - one more time`), reply to a Spotify link, or start playing something on Spotify.");
        response.CommandResponse = CommandResponse.NotFound;

        return response;
    }

    private static ResponseModel SuccessMessage(string description, string thumbnailUrl = null)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.ComponentsV2
        };

        var container = response.ComponentsContainer;
        container.WithAccentColor(DiscordConstants.SpotifyColorGreen);

        if (thumbnailUrl != null)
        {
            container.AddComponent(new ComponentSectionProperties(
                new ComponentSectionThumbnailProperties(new ComponentMediaProperties(thumbnailUrl)))
            {
                Components =
                [
                    new TextDisplayProperties(description)
                ]
            });
        }
        else
        {
            container.WithTextDisplay(description);
        }

        container.WithSeparator();
        container.WithTextDisplay(
            new Random().Next(1, 8) == 1
                ? $"{EmojiProperties.Custom(DiscordConstants.Spotify).ToDiscordString("Spotify")} Spotify remote\n" +
                  $"-# Remote only - controls playback, does not scrobble or track. Last.fm handles your stats."
                : $"{EmojiProperties.Custom(DiscordConstants.Spotify).ToDiscordString("Spotify")} Spotify remote");

        return response;
    }

    private static ResponseModel Cv2Message(Color accentColor, string description)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.ComponentsV2
        };

        var container = response.ComponentsContainer;
        container.WithAccentColor(accentColor);
        container.WithTextDisplay(description);

        return response;
    }

    private static ResponseModel ErrorResponse(RemoteActionResult result)
    {
        switch (result)
        {
            case RemoteActionResult.PremiumRequired:
            {
                var response = Cv2Message(DiscordConstants.WarningColorOrange,
                    "⚠️ **Spotify Premium required**\n\n" +
                    "Spotify only lets apps control playback (queue, skip, play/pause) for **Premium** accounts. Liking songs still works without Premium.");
                response.CommandResponse = CommandResponse.NoPermission;
                return response;
            }
            case RemoteActionResult.NoActiveDevice:
            {
                var response = Cv2Message(DiscordConstants.WarningColorOrange,
                    "📱 **No active Spotify device**\n\n" +
                    "Open Spotify and start playing something, then try again.");
                response.CommandResponse = CommandResponse.NotFound;
                return response;
            }
            case RemoteActionResult.NotConnected:
            {
                var response = NotConnectedResponse();
                response.CommandResponse = CommandResponse.UsernameNotSet;
                return response;
            }
            case RemoteActionResult.Restriction:
            {
                var response = Cv2Message(DiscordConstants.WarningColorOrange,
                    "⚠️ Spotify couldn't do that right now.\n\n" +
                    "The current playback doesn't allow it - for example, there may be no track to skip to.");
                response.CommandResponse = CommandResponse.NotFound;
                return response;
            }
            default:
            {
                var response = Cv2Message(DiscordConstants.WarningColorOrange,
                    "⚠️ Couldn't reach Spotify just now. Please try again in a moment.");
                response.CommandResponse = CommandResponse.Error;
                return response;
            }
        }
    }
}
