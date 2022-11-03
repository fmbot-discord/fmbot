using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using FMBot.Bot.Attributes;
using FMBot.Bot.Extensions;
using FMBot.Bot.Interfaces;
using FMBot.Bot.Services;
using FMBot.Bot.Services.ThirdParty;
using FMBot.Domain.Models;
using FMBot.LastFM.Repositories;
using Microsoft.Extensions.Options;

namespace FMBot.Bot.TextCommands;

[Name("Genius")]
public class GeniusCommands : BaseCommandModule
{
    private readonly GeniusService _geniusService;
    private readonly IPrefixService _prefixService;
    private readonly LastFmRepository _lastFmRepository;
    private readonly UserService _userService;

    public GeniusCommands(
        GeniusService geniusService,
        IPrefixService prefixService,
        LastFmRepository lastFmRepository,
        UserService userService,
        IOptions<BotSettings> botSettings) : base(botSettings)
    {
        this._geniusService = geniusService;
        this._lastFmRepository = lastFmRepository;
        this._prefixService = prefixService;
        this._userService = userService;
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

                var recentScrobbles = await this._lastFmRepository.GetRecentTracksAsync(userSettings.UserNameLastFM, 1, useCache: true, sessionKey: sessionKey);

                if (await GenericEmbedService.RecentScrobbleCallFailedReply(recentScrobbles, userSettings.UserNameLastFM, this.Context))
                {
                    return;
                }

                var currentTrack = recentScrobbles.Content.RecentTracks[0];
                querystring = $"{currentTrack.ArtistName} {currentTrack.TrackName}";

                currentTrackName = currentTrack.TrackName;
                currentTrackArtist = currentTrack.ArtistName;
            }

            var geniusResults = await this._geniusService.SearchGeniusAsync(querystring, currentTrackName, currentTrackArtist);

            if (geniusResults != null && geniusResults.Any())
            {
                var rnd = new Random();
                if (rnd.Next(0, 7) == 1 && string.IsNullOrWhiteSpace(searchValue))
                {
                    this._embedFooter.WithText($"Tip: Search for other songs by simply adding the searchvalue behind '{prfx}genius'.");
                    this._embed.WithFooter(this._embedFooter);
                }

                var firstResult = geniusResults.First().Result;
                if (firstResult.TitleWithFeatured.Trim().ToLower().StartsWith(currentTrackName.Trim().ToLower()) &&
                    firstResult.PrimaryArtist.Name.Trim().ToLower().Equals(currentTrackArtist.Trim().ToLower()) ||
                    geniusResults.Count == 1)
                {
                    this._embed.WithTitle(firstResult.TitleWithFeatured);
                    this._embed.WithUrl(firstResult.Url);
                    this._embed.WithThumbnailUrl(firstResult.SongArtImageThumbnailUrl);

                    this._embed.WithDescription($"By **[{firstResult.PrimaryArtist.Name}]({firstResult.PrimaryArtist.Url})**");

                    var components = new ComponentBuilder().WithButton("View on Genius", style: ButtonStyle.Link, url: firstResult.Url);

                    await ReplyAsync("", false, this._embed.Build(), components: components.Build());
                    this.Context.LogCommandUsed();
                    return;
                }

                this._embed.WithTitle($"Genius results for {querystring}");
                this._embed.WithThumbnailUrl(firstResult.SongArtImageThumbnailUrl);

                var embedDescription = new StringBuilder();

                var amount = geniusResults.Count > 5 ? 5 : geniusResults.Count;
                for (var i = 0; i < amount; i++)
                {
                    var geniusResult = geniusResults[i].Result;

                    embedDescription.AppendLine($"{i + 1}. [{geniusResult.TitleWithFeatured}]({geniusResult.Url})");
                    embedDescription.AppendLine($"By **[{geniusResult.PrimaryArtist.Name}]({geniusResult.PrimaryArtist.Url})**");
                    embedDescription.AppendLine();
                }

                this._embed.WithDescription(embedDescription.ToString());

                await ReplyAsync("", false, this._embed.Build());

                this.Context.LogCommandUsed();
            }
            else
            {
                await ReplyAsync("No results have been found for this track.");
                this.Context.LogCommandUsed(CommandResponse.NotFound);
            }
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }
}
