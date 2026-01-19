using System;
using System.Threading.Tasks;
using FMBot.Bot.Attributes;
using FMBot.Bot.Extensions;
using FMBot.Bot.Models;
using FMBot.Bot.Services;
using FMBot.Bot.Services.ThirdParty;
using FMBot.Domain;
using FMBot.Domain.Interfaces;
using FMBot.Domain.Models;
using NetCord.Services.ApplicationCommands;
using NetCord;
using NetCord.Rest;
using Fergun.Interactive;

namespace FMBot.Bot.SlashCommands;

public class AppleMusicSlashCommands(
    AppleMusicService appleMusicService,
    UserService userService,
    IDataSourceFactory dataSourceFactory,
    InteractiveService interactivity)
    : ApplicationCommandModule<ApplicationCommandContext>
{
    private InteractiveService Interactivity { get; } = interactivity;

    [SlashCommand("applemusic", "Search through Apple Music.",
        Contexts = [InteractionContextType.BotDMChannel, InteractionContextType.DMChannel, InteractionContextType.Guild],
        IntegrationTypes = [ApplicationIntegrationType.UserInstall, ApplicationIntegrationType.GuildInstall])]
    [UsernameSetRequired]
    public async Task AppleMusicAsync(
        [SlashCommandParameter(Name = "search", Description = "Search value")]
        string searchValue = null,
        [SlashCommandParameter(Name = "private", Description = "Only show response to you")]
        bool privateResponse = false)
    {
        await Context.Interaction.SendResponseAsync(InteractionCallback.DeferredMessage(privateResponse ? MessageFlags.Ephemeral : default));

        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);

        try
        {
            if (searchValue != null && searchValue.StartsWith("play ", StringComparison.OrdinalIgnoreCase))
            {
                searchValue = searchValue.Replace("play ", "", StringComparison.OrdinalIgnoreCase);
            }

            string querystring;
            if (!string.IsNullOrWhiteSpace(searchValue))
            {
                querystring = searchValue;
            }
            else
            {
                string sessionKey = null;
                if (!string.IsNullOrEmpty(contextUser.SessionKeyLastFm))
                {
                    sessionKey = contextUser.SessionKeyLastFm;
                }

                var recentScrobbles = await dataSourceFactory.GetRecentTracksAsync(contextUser.UserNameLastFM, 1, useCache: true, sessionKey: sessionKey);

                if (GenericEmbedService.RecentScrobbleCallFailed(recentScrobbles))
                {
                    var errorResponse = GenericEmbedService.RecentScrobbleCallFailedResponse(recentScrobbles, contextUser.UserNameLastFM);

                    await this.Context.SendResponse(this.Interactivity, errorResponse, userService);
                    await this.Context.LogCommandUsedAsync(errorResponse, userService);
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

            await this.Context.SendFollowUpResponse(this.Interactivity, response, userService, privateResponse);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }
}
