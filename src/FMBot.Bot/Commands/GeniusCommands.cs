using System;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;

namespace FMBot.Bot.Commands
{
    public class GeniusCommands : ModuleBase
    {
        private readonly EmbedBuilder _embed;
        private readonly EmbedFooterBuilder _embedFooter;

        private readonly LastFMService _lastFmService = new LastFMService();
        private readonly Logger.Logger _logger;
        private readonly GeniusService _geniusService = new GeniusService();

        private readonly UserService _userService = new UserService();

        public GeniusCommands(Logger.Logger logger)
        {
            this._logger = logger;
            this._embed = new EmbedBuilder()
                .WithColor(Constants.LastFMColorRed);
            this._embedFooter = new EmbedFooterBuilder();
        }

        [Command("fmgenius")]
        [Summary("Shares a link to the Genius lyrics based on what a user is listening to")]
        [Alias("fmlyrics", "fmg", "fmlyricsfind", "fmlyricsearch", "fmlyricssearch")]
        public async Task GeniusAsync(params string[] searchValues)
        {
            var userSettings = await this._userService.GetUserSettingsAsync(this.Context.User);

            if (userSettings?.UserNameLastFM == null)
            {
                this._embed.UsernameNotSetErrorResponse(this.Context, this._logger);
                await ReplyAsync("", false, this._embed.Build());
                return;
            }

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
                        this._embed.NoScrobblesFoundErrorResponse(tracks.Status, this.Context, this._logger);
                        await ReplyAsync("", false, this._embed.Build());
                        return;
                    }

                    var currentTrack = tracks.Content[0];
                    querystring = $"{currentTrack.ArtistName} {currentTrack.Name}";
                }

                var songResult = await this._geniusService.GetUrlAsync(querystring);

                if (songResult != null)
                {
                    this._embed.WithTitle(songResult.Url);
                    this._embed.WithThumbnailUrl(songResult.SongArtImageThumbnailUrl);

                    this._embed.AddField(
                        $"{songResult.TitleWithFeatured}",
                        $"By **[{songResult.PrimaryArtist.Name}]({songResult.PrimaryArtist.Url})**");

                    var rnd = new Random();
                    if (rnd.Next(0, 5) == 1 && searchValues.Length < 1)
                    {
                        this._embedFooter.WithText("Tip: Search for other songs by simply adding the searchvalue behind .fmgenius.");
                        this._embed.WithFooter(this._embedFooter);
                    }

                    await ReplyAsync("", false, this._embed.Build());

                    this._logger.LogCommandUsed(this.Context.Guild?.Id, this.Context.Channel.Id,
                        this.Context.User.Id, this.Context.Message.Content);
                }
                else
                {
                    await ReplyAsync("No results have been found for this track.");
                }
            }
            catch (Exception e)
            {
                this._logger.LogException(this.Context.Message.Content, e);
                await ReplyAsync(
                    "Unable to show Last.FM info via Genius due to an internal error. " +
                    "Try setting a Last.FM name with the 'fmset' command, scrobbling something, and then use the command again.");
            }
        }
    }
}
