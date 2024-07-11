using Discord.Commands;
using FMBot.Bot.Attributes;
using FMBot.Bot.Extensions;
using FMBot.Bot.Models;
using FMBot.Bot.Services;
using System.Threading.Tasks;
using System;
using FMBot.Bot.Interfaces;
using FMBot.Bot.Services.ThirdParty;
using FMBot.Domain;
using FMBot.Domain.Interfaces;
using FMBot.Domain.Models;
using Microsoft.Extensions.Options;
using Fergun.Interactive;

namespace FMBot.Bot.TextCommands;

[Name("AppleMusic")]
public class AppleMusicCommands : BaseCommandModule
{
    private readonly IDataSourceFactory _dataSourceFactory;
    private readonly AppleMusicService _appleMusicService;

    private readonly UserService _userService;
    private readonly IPrefixService _prefixService;

    private InteractiveService Interactivity { get; }


    public AppleMusicCommands(IDataSourceFactory dataSourceFactory, AppleMusicService appleMusicService, UserService userService, IPrefixService prefixService, IOptions<BotSettings> botSettings, InteractiveService interactivity) : base(botSettings)
    {
        this._dataSourceFactory = dataSourceFactory;
        this._appleMusicService = appleMusicService;
        this._userService = userService;
        this._prefixService = prefixService;
        this.Interactivity = interactivity;
    }

    [Command("applemusic")]
    [Summary("Shares a link to an Apple Music track based on what a user is listening to or searching for")]
    [Alias("am", "apple")]
    [UsernameSetRequired]
    [CommandCategories(CommandCategory.ThirdParty)]
    public async Task AppleMusicAsync([Remainder] string searchValue = null)
    {
        var userSettings = await this._userService.GetUserSettingsAsync(this.Context.User);
        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);

        try
        {
            _ = this.Context.Channel.TriggerTypingAsync();

            if (searchValue != null && searchValue.StartsWith("play ", StringComparison.OrdinalIgnoreCase))
            {
                searchValue = searchValue.Replace("play ", "", StringComparison.OrdinalIgnoreCase);
            }

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

                querystring = $"{currentTrack.TrackName} {currentTrack.ArtistName} {currentTrack.AlbumName}";
            }

            var item = await this._appleMusicService.SearchAppleMusicSong(querystring);

            var response = new ResponseModel
            {
                ResponseType = ResponseType.Text
            };

            if (item != null)
            {
                response.Text = $"{item.Attributes.Url}";

                var rnd = new Random();
                if (rnd.Next(0, 8) == 1 && string.IsNullOrWhiteSpace(searchValue) && !await this._userService.HintShownBefore(userSettings.UserId, "applemusic"))
                {
                    response.Text += $"\n-# *Tip: Search for other songs by simply adding the searchvalue behind {prfx}applemusic.*";
                    response.HintShown = true;
                }

                PublicProperties.UsedCommandsArtists.TryAdd(this.Context.Message.Id, item.Attributes.ArtistName);
                PublicProperties.UsedCommandsTracks.TryAdd(this.Context.Message.Id, item.Attributes.Name);
            }
            else
            {

                response.Text = $"Sorry, Apple Music returned no results for *`{StringExtensions.Sanitize(querystring)}`*.";
                response.CommandResponse = CommandResponse.NotFound;
            }

            await this.Context.SendResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }
}
