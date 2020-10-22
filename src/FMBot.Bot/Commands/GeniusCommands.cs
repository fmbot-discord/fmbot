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
using FMBot.Domain;
using FMBot.Domain.Models;
using FMBot.LastFM.Services;

namespace FMBot.Bot.Commands
{
    public class GeniusCommands : ModuleBase
    {
        private readonly EmbedBuilder _embed;
        private readonly EmbedFooterBuilder _embedFooter;

        private readonly LastFMService _lastFmService;
        private readonly GeniusService _geniusService;

        private readonly UserService _userService;

        private readonly IPrefixService _prefixService;

        public GeniusCommands(
            IPrefixService prefixService,
            LastFMService lastFmService,
            UserService userService,
            GeniusService geniusService)
        {
            this._prefixService = prefixService;
            this._userService = userService;
            this._geniusService = geniusService;
            this._lastFmService = lastFmService;
            this._embed = new EmbedBuilder()
                .WithColor(DiscordConstants.LastFMColorRed);
            this._embedFooter = new EmbedFooterBuilder();
        }

        [Command("genius")]
        [Summary("Shares a link to the Genius lyrics based on what a user is listening to or what the user is searching for.")]
        [Alias("lyrics", "g", "lyricsfind", "lyricsearch", "lyricssearch")]
        [UsernameSetRequired]
        public async Task GeniusAsync(params string[] searchValues)
        {
            var userSettings = await this._userService.GetUserSettingsAsync(this.Context.User);
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id) ?? ConfigData.Data.Bot.Prefix;

            try
            {
                _ = this.Context.Channel.TriggerTypingAsync();

                string querystring;
                if (searchValues.Length > 0)
                {
                    querystring = string.Join(" ", searchValues);
                }
                else
                {
                    var tracks = await this._lastFmService.GetRecentScrobblesAsync(userSettings.UserNameLastFM, 1);

                    if (tracks?.Any() != true)
                    {
                        this._embed.NoScrobblesFoundErrorResponse(tracks.Status, prfx, userSettings.UserNameLastFM);
                        await ReplyAsync("", false, this._embed.Build());
                        this.Context.LogCommandUsed(CommandResponse.NoScrobbles);
                        return;
                    }

                    var currentTrack = tracks.Content[0];
                    querystring = $"{currentTrack.ArtistName} {currentTrack.Name}";
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
                    if (rnd.Next(0, 5) == 1 && searchValues.Length < 1)
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
                    "Unable to show Last.FM info via Genius due to an internal error. " +
                    "Try setting a Last.FM name with the 'fmset' command, scrobbling something, and then use the command again.");
            }
        }
    }
}
