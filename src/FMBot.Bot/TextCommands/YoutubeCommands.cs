using System;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Fergun.Interactive;
using FMBot.Bot.Attributes;
using FMBot.Bot.Extensions;
using FMBot.Bot.Interfaces;
using FMBot.Bot.Models;
using FMBot.Bot.Services;
using FMBot.Bot.Services.ThirdParty;
using FMBot.Domain;
using FMBot.Domain.Interfaces;
using FMBot.Domain.Models;
using Microsoft.Extensions.Options;

namespace FMBot.Bot.TextCommands;

[Name("Youtube")]
public class YoutubeCommands : BaseCommandModule
{
    private readonly IDataSourceFactory _dataSourceFactory;
    private readonly UserService _userService;
    private readonly YoutubeService _youtubeService;

    private readonly IPrefixService _prefixService;

    private InteractiveService Interactivity { get; }


    public YoutubeCommands(
        IPrefixService prefixService,
        IDataSourceFactory dataSourceFactory,
        UserService userService,
        YoutubeService youtubeService,
        IOptions<BotSettings> botSettings, InteractiveService interactivity) : base(botSettings)
    {
        this._prefixService = prefixService;
        this._userService = userService;
        this._youtubeService = youtubeService;
        this.Interactivity = interactivity;
        this._dataSourceFactory = dataSourceFactory;
    }

    [Command("youtube")]
    [Summary("Shares a link to a YouTube video based on what a user is listening to or searching for")]
    [Alias("yt", "y", "youtubesearch", "ytsearch", "yts")]
    [UsernameSetRequired]
    [CommandCategories(CommandCategory.ThirdParty)]
    public async Task YoutubeAsync([Remainder] string searchValue = null)
    {
        var userSettings = await this._userService.GetUserSettingsAsync(this.Context.User);
        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);

        try
        {
            _ = this.Context.Channel.TriggerTypingAsync();

            if (this.Context.Message.ReferencedMessage != null && string.IsNullOrWhiteSpace(searchValue))
            {
                var internalLookup = CommandContextExtensions.GetReferencedMusic(this.Context.Message.ReferencedMessage.Id)
                                     ??
                                     await this._userService.GetReferencedMusic(this.Context.Message.ReferencedMessage.Id);

                if (internalLookup?.Track != null)
                {
                    searchValue = $"{internalLookup.Artist} | {internalLookup.Track}";
                }
            }

            string querystring;
            if (!string.IsNullOrWhiteSpace(searchValue))
            {
                querystring = searchValue;
            }
            else
            {
                string sessionKey = null;
                if (!string.IsNullOrEmpty(userSettings.SessionKeyLastFm))
                {
                    sessionKey = userSettings.SessionKeyLastFm;
                }

                var recentScrobbles = await this._dataSourceFactory.GetRecentTracksAsync(userSettings.UserNameLastFM, 1, useCache: true, sessionKey: sessionKey);

                if (await GenericEmbedService.RecentScrobbleCallFailedReply(recentScrobbles, userSettings.UserNameLastFM, this.Context))
                {
                    return;
                }

                var currentTrack = recentScrobbles.Content.RecentTracks[0];
                querystring = currentTrack.TrackName + " - " + currentTrack.ArtistName;

                PublicProperties.UsedCommandsArtists.TryAdd(this.Context.Message.Id, currentTrack.ArtistName);
                PublicProperties.UsedCommandsTracks.TryAdd(this.Context.Message.Id, currentTrack.TrackName);
                if (!string.IsNullOrWhiteSpace(currentTrack.AlbumName))
                {
                    PublicProperties.UsedCommandsAlbums.TryAdd(this.Context.Message.Id, currentTrack.AlbumName);
                }
            }

            try
            {
                var response = new ResponseModel
                {
                    ResponseType = ResponseType.Text
                };

                var youtubeResult = await this._youtubeService.GetSearchResult(querystring);

                if (youtubeResult == null)
                {
                    response.Text = "No results have been found for this query.";
                    response.CommandResponse = CommandResponse.NotFound;

                    await this.Context.SendResponse(this.Interactivity, response);
                    this.Context.LogCommandUsed(response.CommandResponse);
                    return;
                }

                var name = await UserService.GetNameAsync(this.Context.Guild, this.Context.User);

                response.Text = $"{StringExtensions.Sanitize(name)} searched for: `{StringExtensions.Sanitize(querystring)}`";

                var video = await this._youtubeService.GetVideoResult(youtubeResult.VideoId);

                var user = await this.Context.Guild.GetUserAsync(this.Context.User.Id);
                if (user.GuildPermissions.EmbedLinks)
                {
                    if (video is { IsFamilyFriendly: true })
                    {
                        response.Text += $"\nhttps://youtube.com/watch?v={youtubeResult.VideoId}";
                    }
                    else
                    {
                        response.Text += $"\n<https://youtube.com/watch?v={youtubeResult.VideoId}>" +
                                         $"\n`{youtubeResult.Title}`" +
                                         $"\n" +
                                         $"-# *Embed disabled because video might not be SFW.*";
                    }
                }
                else
                {
                    response.Text += $"\n<https://youtube.com/watch?v={youtubeResult.VideoId}>" +
                                     $"\n`{youtubeResult.Title}`" +
                                     $"\n-# *Embed disabled because user that requested link is not allowed to embed links.*";
                }

                var rnd = new Random();
                if (rnd.Next(0, 8) == 1 && string.IsNullOrWhiteSpace(searchValue) && !await this._userService.HintShownBefore(userSettings.UserId, "youtube"))
                {
                    response.Text += $"\n-# *Tip: Search for other songs or videos by simply adding the searchvalue behind {prfx}youtube.*";
                    response.HintShown = true;
                }

                await this.Context.SendResponse(this.Interactivity, response);
                this.Context.LogCommandUsed(response.CommandResponse);
            }
            catch (Exception e)
            {
                await this.Context.HandleCommandException(e, sendReply: false);
                await ReplyAsync("No YouTube results have been found for this query.\n" +
                                 "It could also be that we've currently exceeded the YouTube ratelimits.");
            }
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }
}
