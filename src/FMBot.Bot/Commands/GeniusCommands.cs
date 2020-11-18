using System;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using FMBot.Bot.Attributes;
using FMBot.Bot.Configurations;
using FMBot.Bot.Extensions;
using FMBot.Bot.Interfaces;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Domain.Models;
using FMBot.LastFM.Services;

namespace FMBot.Bot.Commands
{
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

                string querystring;
                if (!string.IsNullOrWhiteSpace(searchValue))
                {
                    querystring = searchValue;
                }
                else
                {
                    var recentScrobbles = await this._lastFmService.GetRecentTracksAsync(userSettings.UserNameLastFM, 1, useCache: true);

                    if (!recentScrobbles.Success || recentScrobbles.Content == null)
                    {
                        this._embed.ErrorResponse(recentScrobbles.Error, recentScrobbles.Message, this.Context);
                        this.Context.LogCommandUsed(CommandResponse.LastFmError);
                        await ReplyAsync("", false, this._embed.Build());
                        return;
                    }

                    if (!recentScrobbles.Content.RecentTracks.Track.Any())
                    {
                        this._embed.NoScrobblesFoundErrorResponse(userSettings.UserNameLastFM);
                        this.Context.LogCommandUsed(CommandResponse.NoScrobbles);
                        await ReplyAsync("", false, this._embed.Build());
                        return;
                    }

                    var currentTrack = recentScrobbles.Content.RecentTracks.Track[0];
                    querystring = $"{currentTrack.Artist.Text} {currentTrack.Name}";
                }

                var songResult = await this._geniusService.SearchGeniusAsync(querystring);

                if (songResult != null)
                {
                    this._embed.WithTitle(songResult.Result.Url);
                    this._embed.WithThumbnailUrl(songResult.Result.SongArtImageThumbnailUrl);

                    this._embed.AddField(
                        $"{songResult.Result.TitleWithFeatured}",
                        $"By **[{songResult.Result.PrimaryArtist.Name}]({songResult.Result.PrimaryArtist.Url})**");

                    var rnd = new Random();
                    if (rnd.Next(0, 7) == 1 && string.IsNullOrWhiteSpace(searchValue))
                    {
                        this._embedFooter.WithText("Tip: Search for other songs by simply adding the searchvalue behind .fmgenius.");
                        this._embed.WithFooter(this._embedFooter);
                    }

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
                    "Try setting a Last.fm name with the 'fmset' command, scrobbling something, and then use the command again.");
            }
        }
    }
}
