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
using NetCord.Services.Commands;
using Fergun.Interactive;

namespace FMBot.Bot.TextCommands;

[ModuleName("AppleMusic")]
public class AppleMusicCommands(
    IDataSourceFactory dataSourceFactory,
    AppleMusicService appleMusicService,
    UserService userService,
    IPrefixService prefixService,
    IOptions<BotSettings> botSettings,
    InteractiveService interactivity)
    : BaseCommandModule(botSettings)
{
    private InteractiveService Interactivity { get; } = interactivity;


    [Command("applemusic", "am", "apple")]
    [Summary("Shares a link to an Apple Music track based on what a user is listening to or searching for")]
    [UsernameSetRequired]
    [CommandCategories(CommandCategory.ThirdParty)]
    public async Task AppleMusicAsync([CommandParameter(Remainder = true)] string searchValue = null)
    {
        var userSettings = await userService.GetUserSettingsAsync(this.Context.User);
        var prfx = prefixService.GetPrefix(this.Context.Guild?.Id);

        try
        {
            _ = this.Context.Channel?.TriggerTypingStateAsync()!;

            if (searchValue != null && searchValue.StartsWith("play ", StringComparison.OrdinalIgnoreCase))
            {
                searchValue = searchValue.Replace("play ", "", StringComparison.OrdinalIgnoreCase);
            }

            if (this.Context.Message.ReferencedMessage != null && string.IsNullOrWhiteSpace(searchValue))
            {
                var internalLookup = CommandContextExtensions.GetReferencedMusic(this.Context.Message.ReferencedMessage.Id)
                                     ??
                                     await userService.GetReferencedMusic(this.Context.Message.ReferencedMessage.Id);

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

                var recentScrobbles = await dataSourceFactory.GetRecentTracksAsync(userSettings.UserNameLastFM, 1, useCache: true, sessionKey: sessionKey);

                if (await GenericEmbedService.RecentScrobbleCallFailedReply(recentScrobbles, userSettings.UserNameLastFM, this.Context, userService))
                {
                    return;
                }

                var currentTrack = recentScrobbles.Content.RecentTracks[0];

                querystring = $"{currentTrack.TrackName} {currentTrack.ArtistName} {currentTrack.AlbumName}";
            }

            var item = await appleMusicService.SearchAppleMusicSong(querystring);

            var response = new ResponseModel
            {
                ResponseType = ResponseType.Text
            };

            if (item != null)
            {
                response.Text = $"{item.Attributes.Url}";

                var rnd = new Random();
                if (rnd.Next(0, 8) == 1 && string.IsNullOrWhiteSpace(searchValue) && !await userService.HintShownBefore(userSettings.UserId, "applemusic"))
                {
                    response.Text += $"\n-# *Tip: Search for other songs by simply adding the searchvalue behind {prfx}applemusic.*";
                    response.HintShown = true;
                }

                response.ReferencedMusic = new ReferencedMusic
                {
                    Artist = item.Attributes.ArtistName,
                    Track = item.Attributes.Name
                };
            }
            else
            {
                response.Text = $"Sorry, Apple Music returned no results for *`{StringExtensions.Sanitize(querystring)}`*.";
                response.CommandResponse = CommandResponse.NotFound;
            }

            await this.Context.SendResponse(this.Interactivity, response, userService);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }
}
