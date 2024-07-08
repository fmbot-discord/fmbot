using System;
using System.Linq;
using System.Text;
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
using FMBot.LastFM.Repositories;
using Microsoft.Extensions.Options;

namespace FMBot.Bot.TextCommands;

[Name("Genius")]
public class GeniusCommands : BaseCommandModule
{
    private readonly GeniusService _geniusService;
    private readonly IPrefixService _prefixService;
    private readonly IDataSourceFactory _dataSourceFactory;
    private readonly UserService _userService;

    private InteractiveService Interactivity { get; }

    public GeniusCommands(
        GeniusService geniusService,
        IPrefixService prefixService,
        IDataSourceFactory dataSourceFactory,
        UserService userService,
        IOptions<BotSettings> botSettings, InteractiveService interactivity) : base(botSettings)
    {
        this._geniusService = geniusService;
        this._dataSourceFactory = dataSourceFactory;
        this._prefixService = prefixService;
        this._userService = userService;
        this.Interactivity = interactivity;
    }

    [Command("genius")]
    [Summary("Shares a link to the Genius lyrics based on what a user is listening to or what the user is searching for.")]
    [Alias("lyrics", "lyr", "lr", "gen", "lyricsfind", "lyricsearch", "lyricssearch")]
    [UsernameSetRequired]
    [CommandCategories(CommandCategory.ThirdParty)]
    public async Task GeniusAsync([Remainder] string searchValue = null)
    {
        _ = this.Context.Channel.TriggerTypingAsync();

        var userSettings = await this._userService.GetUserSettingsAsync(this.Context.User);
        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);

        try
        {
            var currentTrackName = "";
            var currentTrackArtist = "";

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
                querystring = $"{currentTrack.ArtistName} {currentTrack.TrackName}";

                currentTrackName = currentTrack.TrackName;
                currentTrackArtist = currentTrack.ArtistName;

                PublicProperties.UsedCommandsArtists.TryAdd(this.Context.Message.Id, currentTrack.ArtistName);
                PublicProperties.UsedCommandsTracks.TryAdd(this.Context.Message.Id, currentTrack.TrackName);
                if (!string.IsNullOrWhiteSpace(currentTrack.AlbumName))
                {
                    PublicProperties.UsedCommandsAlbums.TryAdd(this.Context.Message.Id, currentTrack.AlbumName);
                }
            }

            var response = new ResponseModel
            {
                ResponseType = ResponseType.Embed
            };

            var geniusResults = await this._geniusService.SearchGeniusAsync(querystring, currentTrackName, currentTrackArtist);

            if (geniusResults != null && geniusResults.Any())
            {
                var rnd = new Random();
                if (rnd.Next(0, 8) == 1 && string.IsNullOrWhiteSpace(searchValue) && !await this._userService.HintShownBefore(userSettings.UserId, "genius"))
                {
                    response.EmbedFooter.WithText($"Tip: Search for other songs by simply adding the searchvalue behind '{prfx}genius'.");
                    response.HintShown = true;
                    response.Embed.WithFooter(response.EmbedFooter);
                }

                var firstResult = geniusResults.First().Result;
                if (firstResult.TitleWithFeatured.Trim().StartsWith(currentTrackName.Trim(), StringComparison.OrdinalIgnoreCase) &&
                    firstResult.PrimaryArtist.Name.Trim().Equals(currentTrackArtist.Trim(), StringComparison.OrdinalIgnoreCase) ||
                    geniusResults.Count == 1)
                {
                    response.Embed.WithTitle(firstResult.TitleWithFeatured);
                    response.Embed.WithUrl(firstResult.Url);
                    response.Embed.WithThumbnailUrl(firstResult.SongArtImageThumbnailUrl);

                    response.Embed.WithDescription($"By **[{firstResult.PrimaryArtist.Name}]({firstResult.PrimaryArtist.Url})**");

                    response.Components = new ComponentBuilder().WithButton("View on Genius", style: ButtonStyle.Link, url: firstResult.Url);

                    await this.Context.SendResponse(this.Interactivity, response);
                    this.Context.LogCommandUsed(response.CommandResponse);
                    return;
                }

                response.Embed.WithTitle($"Genius results for {querystring}");
                response.Embed.WithThumbnailUrl(firstResult.SongArtImageThumbnailUrl);

                var embedDescription = new StringBuilder();

                var amount = geniusResults.Count > 5 ? 5 : geniusResults.Count;
                for (var i = 0; i < amount; i++)
                {
                    var geniusResult = geniusResults[i].Result;

                    embedDescription.AppendLine($"{i + 1}. [{geniusResult.TitleWithFeatured}]({geniusResult.Url})");
                    embedDescription.AppendLine($"By **[{geniusResult.PrimaryArtist.Name}]({geniusResult.PrimaryArtist.Url})**");
                    embedDescription.AppendLine();
                }

                response.Embed.WithDescription(embedDescription.ToString());
            }
            else
            {
                response.Embed.WithDescription("No Genius results have been found for this track.");
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
