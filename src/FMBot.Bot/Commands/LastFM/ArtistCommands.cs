using System;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Data.Entities;

namespace FMBot.Bot.Commands.LastFM
{
    public class ArtistCommands : ModuleBase
    {
        private readonly EmbedBuilder _embed;
        private readonly EmbedAuthorBuilder _embedAuthor;
        private readonly EmbedFooterBuilder _embedFooter;
        private readonly GuildService _guildService = new GuildService();
        private readonly LastFMService _lastFmService = new LastFMService();
        private readonly Logger.Logger _logger;

        private readonly UserService _userService = new UserService();

        public ArtistCommands(Logger.Logger logger)
        {
            this._logger = logger;
            this._embed = new EmbedBuilder()
                .WithColor(Constants.LastFMColorRed);
            this._embedAuthor = new EmbedAuthorBuilder();
            this._embedFooter = new EmbedFooterBuilder();
        }

        [Command("fmartist", RunMode = RunMode.Async)]
        [Summary("Displays current artist.")]
        [Alias("fmartist", "fma")]
        public async Task ArtistsAsync(string artist)
        {
            var userSettings = await this._userService.GetUserSettingsAsync(this.Context.User);

            if (userSettings?.UserNameLastFM == null)
            {
                await UsernameNotSetErrorResponseAsync();
                return;
            }

            if (artist == "help")
            {
                await ReplyAsync(
                    "Usage: `.fmartist 'name'\n" +
                    "If you don't enter any artists name, it will get the info from the artist you're currently listening to.");
                return;
            }

            var artists = await this._lastFmService.GetArtistInfoAsync(artist, userSettings.UserNameLastFM);

            if (!artists.Success)
            {
                this._embed.NoScrobblesFoundErrorResponse(artists.Status, this.Context, this._logger);
                await ReplyAsync("", false, this._embed.Build());
                return;
            }

            string userTitle;

            userTitle = await this._userService.GetUserTitleAsync(this.Context);


            this._embedAuthor.WithIconUrl(this.Context.User.GetAvatarUrl());
            this._embedAuthor.WithName($"Info about  for {userTitle}");
            this._embedAuthor.WithUrl(Constants.LastFMUserUrl + userSettings.UserNameLastFM + "/library/artists");
            this._embed.WithAuthor(this._embedAuthor);

            var description = "";

            this._embed.WithDescription(description);


            await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
            this._logger.LogCommandUsed(this.Context.Guild?.Id, this.Context.Channel.Id, this.Context.User.Id,
                this.Context.Message.Content);
        }

        [Command("fmartists", RunMode = RunMode.Async)]
        [Summary("Displays top artists.")]
        [Alias("fmal", "fmartistlist", "fmartistslist")]
        public async Task ArtistsAsync(string time = "weekly", int num = 10, string user = null)
        {
            var userSettings = await this._userService.GetUserSettingsAsync(this.Context.User);

            if (userSettings?.UserNameLastFM == null)
            {
                await UsernameNotSetErrorResponseAsync();
                return;
            }

            if (time == "help")
            {
                await ReplyAsync(
                    "Usage: `.fmartists 'weekly/monthly/yearly/alltime' 'number of artists (max 10)' 'lastfm username/discord user'` \n" +
                    "You can set your default user and your display mode through the `.fmset 'username' 'embedfull/embedmini/textfull/textmini'` command.");
                return;
            }

            if (!Enum.TryParse(time, true, out ChartTimePeriod timePeriod))
            {
                await ReplyAsync("Invalid time period. Please use 'weekly', 'monthly', 'yearly', or 'alltime'. \n" +
                                 "Usage: `.fmartists 'weekly/monthly/yearly/alltime' 'number of artists (max 10)' 'lastfm username/discord user'`");
                return;
            }

            if (num > 20)
            {
                num = 20;
            }

            if (num < 1)
            {
                num = 1;
            }

            var timeSpan = this._lastFmService.GetLastStatsTimeSpan(timePeriod);

            try
            {
                var lastFMUserName = userSettings.UserNameLastFM;
                var self = true;

                if (user != null)
                {
                    var alternativeLastFmUserName = await FindUser(user);
                    if (!string.IsNullOrEmpty(alternativeLastFmUserName))
                    {
                        lastFMUserName = alternativeLastFmUserName;
                        self = false;
                    }
                }

                var artists = await this._lastFmService.GetTopArtistsAsync(lastFMUserName, timeSpan, num);

                if (artists?.Any() != true)
                {
                    this._embed.NoScrobblesFoundErrorResponse(artists.Status, this.Context, this._logger);
                    await ReplyAsync("", false, this._embed.Build());
                    return;
                }

                string userTitle;
                if (self)
                {
                    userTitle = await this._userService.GetUserTitleAsync(this.Context);
                }
                else
                {
                    userTitle =
                        $"{lastFMUserName}, requested by {await this._userService.GetUserTitleAsync(this.Context)}";
                }

                this._embedAuthor.WithIconUrl(this.Context.User.GetAvatarUrl());
                var artistsString = num == 1 ? "artist" : "artists";
                this._embedAuthor.WithName($"Top {num} {timePeriod} {artistsString} for {userTitle}");
                this._embedAuthor.WithUrl(Constants.LastFMUserUrl + lastFMUserName + "/library/artists");
                this._embed.WithAuthor(this._embedAuthor);

                var description = "";
                for (var i = 0; i < artists.Count(); i++)
                {
                    var artist = artists.Content[i];

                    description += $"{i + 1}. [{artist.Name}]({artist.Url}) ({artist.PlayCount} plays) \n";
                }

                this._embed.WithDescription(description);

                var userInfo = await this._lastFmService.GetUserInfoAsync(lastFMUserName);

                this._embedFooter.WithText(lastFMUserName + "'s total scrobbles: " +
                                           userInfo.Content.Playcount.ToString("N0"));
                this._embed.WithFooter(this._embedFooter);

                await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
                this._logger.LogCommandUsed(this.Context.Guild?.Id, this.Context.Channel.Id, this.Context.User.Id,
                    this.Context.Message.Content);
            }
            catch (Exception e)
            {
                this._logger.LogError(e.Message, this.Context.Message.Content, this.Context.User.Username,
                    this.Context.Guild?.Name, this.Context.Guild?.Id);
                await ReplyAsync("Unable to show Last.FM info due to an internal error.");
            }
        }

        private async Task UsernameNotSetErrorResponseAsync()
        {
            this._embed.UsernameNotSetErrorResponse(this.Context, this._logger);
            await ReplyAsync("", false, this._embed.Build());
        }

        private async Task<string> FindUser(string user)
        {
            if (await this._lastFmService.LastFMUserExistsAsync(user))
            {
                return user;
            }

            if (!this._guildService.CheckIfDM(this.Context))
            {
                var guildUser = await this._guildService.FindUserFromGuildAsync(this.Context, user);

                if (guildUser != null)
                {
                    var guildUserLastFm = await this._userService.GetUserSettingsAsync(guildUser);

                    return guildUserLastFm?.UserNameLastFM;
                }
            }

            return null;
        }
    }
}
