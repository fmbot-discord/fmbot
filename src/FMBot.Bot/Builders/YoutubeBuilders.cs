using System;
using System.Threading.Tasks;
using FMBot.Bot.Extensions;
using FMBot.Bot.Models;
using FMBot.Bot.Services;
using FMBot.Bot.Services.ThirdParty;
using FMBot.Domain;
using FMBot.Domain.Interfaces;
using FMBot.Domain.Models;

namespace FMBot.Bot.Builders;

public class YoutubeBuilders
{
    private readonly YoutubeService _youtubeService;
    private readonly IDataSourceFactory _dataSourceFactory;
    private readonly InteractiveService _interactivity;
    private readonly UserService _userService;


    public YoutubeBuilders(YoutubeService youtubeService, IDataSourceFactory dataSourceFactory,
        InteractiveService interactivity, UserService userService)
    {
        this._youtubeService = youtubeService;
        this._dataSourceFactory = dataSourceFactory;
        this._interactivity = interactivity;
        this._userService = userService;
    }

    public async Task<ResponseModel> YoutubeAsync(ContextModel context, string searchValue)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Text
        };

        if (string.IsNullOrWhiteSpace(searchValue))
        {
            string sessionKey = null;
            if (!string.IsNullOrEmpty(context.ContextUser.SessionKeyLastFm))
            {
                sessionKey = context.ContextUser.SessionKeyLastFm;
            }

            var recentTracks = await this._dataSourceFactory.GetRecentTracksAsync(context.ContextUser.UserNameLastFM, 1,
                useCache: true, sessionKey: sessionKey);

            if (GenericEmbedService.RecentScrobbleCallFailed(recentTracks))
            {
                return GenericEmbedService.RecentScrobbleCallFailedResponse(recentTracks,
                    context.ContextUser.UserNameLastFM);
            }

            var currentTrack = recentTracks.Content.RecentTracks[0];
            searchValue = currentTrack.TrackName + " - " + currentTrack.ArtistName;

            PublicProperties.UsedCommandsArtists.TryAdd(context.InteractionId, currentTrack.ArtistName);
            PublicProperties.UsedCommandsTracks.TryAdd(context.InteractionId, currentTrack.TrackName);
            if (!string.IsNullOrWhiteSpace(currentTrack.AlbumName))
            {
                PublicProperties.UsedCommandsAlbums.TryAdd(context.InteractionId, currentTrack.AlbumName);
            }
        }

        var youtubeResult = await this._youtubeService.GetSearchResult(searchValue);
        if (youtubeResult?.Id?.VideoId == null)
        {
            response.Text = "No results have been found for this query.";
            response.CommandResponse = CommandResponse.NotFound;
            return response;
        }

        var name = await UserService.GetNameAsync(context.DiscordGuild, context.DiscordUser);

        response.Text =
            $"{StringExtensions.Sanitize(name)} searched for: `{StringExtensions.Sanitize(searchValue)}`";

        var videoId = youtubeResult.Id.VideoId;
        var video = await this._youtubeService.GetVideoResult(videoId);

        var user = context.DiscordGuild != null
            ? await context.DiscordGuild.GetUserAsync(context.DiscordUser.Id)
            : null;
        if (user == null || user.GuildPermissions.EmbedLinks)
        {
            if (YoutubeService.IsFamilyFriendly(video))
            {
                response.Text += $"\nhttps://youtube.com/watch?v={videoId}";
            }
            else
            {
                response.Text += $"\n<https://youtube.com/watch?v={videoId}>" +
                                 $"\n`{youtubeResult.Snippet.Title}`" +
                                 $"\n-# *Embed disabled because video is age restricted by YouTube.*";
            }
        }
        else
        {
            response.Text += $"\n<https://youtube.com/watch?v={videoId}>" +
                             $"\n`{youtubeResult.Snippet.Title}`" +
                             $"\n-# *Embed disabled because user that requested link is not allowed to embed links.*";
        }

        var rnd = new Random();
        if (rnd.Next(0, 8) == 1 && string.IsNullOrWhiteSpace(searchValue) &&
            !await this._userService.HintShownBefore(context.ContextUser.UserId, "youtube"))
        {
            response.Text +=
                $"\n-# *Tip: Search for other songs or videos by simply adding the searchvalue behind {context.Prefix}youtube.*";
            response.HintShown = true;
        }

        return response;
    }
}
