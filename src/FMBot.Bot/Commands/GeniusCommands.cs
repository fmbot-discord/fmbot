using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using FMBot.Bot.Attributes;
using FMBot.Bot.Configurations;
using FMBot.Bot.Extensions;
using FMBot.Bot.Interfaces;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Bot.Services.ThirdParty;
using FMBot.Domain.Models;
using FMBot.LastFM.Services;

namespace FMBot.Bot.Commands
{
    [Name("Genius")]
    public class GeniusCommands : ModuleBase
    {
        private readonly GeniusService _geniusService;
        private readonly IPrefixService _prefixService;
        private readonly LastFmService _lastFmService;
        private readonly UserService _userService;

        private readonly EmbedBuilder _embed;
        private readonly EmbedFooterBuilder _embedFooter;

        public GeniusCommands(
                GeniusService geniusService,
                IPrefixService prefixService,
                LastFmService lastFmService,
                UserService userService
            )
        {
            this._geniusService = geniusService;
            this._lastFmService = lastFmService;
            this._prefixService = prefixService;
            this._userService = userService;

            this._embed = new EmbedBuilder()
                .WithColor(DiscordConstants.LastFmColorRed);
            this._embedFooter = new EmbedFooterBuilder();
        }

        [Command("genius")]
        [Summary("Shares a link to the Genius lyrics based on what a user is listening to or what the user is searching for.")]
        [Alias("lyrics", "g", "lyricsfind", "lyricsearch", "lyricssearch")]
        [UsernameSetRequired]
        public async Task GeniusAsync([Remainder] string searchValue = null)
        {
            var userSettings = await this._userService.GetUserSettingsAsync(this.Context.User);
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id) ?? ConfigData.Data.Bot.Prefix;

            try
            {
                _ = this.Context.Channel.TriggerTypingAsync();

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

                    var recentScrobbles = await this._lastFmService.GetRecentTracksAsync(userSettings.UserNameLastFM, 1, useCache: true, sessionKey: sessionKey);

                    if (await ErrorService.RecentScrobbleCallFailedReply(recentScrobbles, userSettings.UserNameLastFM, this.Context))
                    {
                        return;
                    }

                    var currentTrack = recentScrobbles.Content.RecentTracks[0];
                    querystring = $"{currentTrack.ArtistName} {currentTrack.TrackName}";

                    currentTrackName = currentTrack.TrackName;
                    currentTrackArtist = currentTrack.ArtistName;
                }

                var geniusResults = await this._geniusService.SearchGeniusAsync(querystring);

                if (geniusResults != null && geniusResults.Any())
                {
                    var rnd = new Random();
                    if (rnd.Next(0, 7) == 1 && string.IsNullOrWhiteSpace(searchValue))
                    {
                        this._embedFooter.WithText("Tip: Search for other songs by simply adding the searchvalue behind .fmgenius.");
                        this._embed.WithFooter(this._embedFooter);
                    }

                    var firstResult = geniusResults.First().Result;
                    if (firstResult.TitleWithFeatured.ToLower().StartsWith(currentTrackName.ToLower()) &&
                        firstResult.PrimaryArtist.Name.ToLower().Equals(currentTrackArtist.ToLower()) ||
                        geniusResults.Count == 1)
                    {
                        this._embed.WithTitle(firstResult.Url);
                        this._embed.WithThumbnailUrl(firstResult.SongArtImageThumbnailUrl);

                        this._embed.AddField(
                            $"{firstResult.TitleWithFeatured}",
                            $"By **[{firstResult.PrimaryArtist.Name}]({firstResult.PrimaryArtist.Url})**");

                        await ReplyAsync("", false, this._embed.Build());
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
                this.Context.LogCommandException(e);
                await ReplyAsync(
                    "Unable to show Last.fm info via Genius due to an internal error. " +
                    "Please try again later or contact .fmbot support.");
            }
        }
    }
}
