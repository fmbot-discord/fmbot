using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using FMBot.Bot.Configurations;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using static FMBot.Bot.FMBotUtil;

namespace FMBot.Bot.Commands.LastFM
{
    public class ChartCommands : ModuleBase
    {
        private static readonly List<DateTimeOffset> StackCooldownTimer = new List<DateTimeOffset>();
        private static readonly List<SocketUser> StackCooldownTarget = new List<SocketUser>();
        private readonly ChartService _chartService = new ChartService();
        private readonly EmbedBuilder _embed;
        private readonly EmbedAuthorBuilder _embedAuthor;
        private readonly EmbedFooterBuilder _embedFooter;
        private readonly GuildService _guildService = new GuildService();
        private readonly LastFMService _lastFmService = new LastFMService();
        private readonly IPrefixService _prefixService;
        private readonly Logger.Logger _logger;

        private readonly UserService _userService = new UserService();

        public ChartCommands(Logger.Logger logger, IPrefixService prefixService)
        {
            this._logger = logger;
            this._prefixService = prefixService;
            this._embed = new EmbedBuilder()
                .WithColor(Constants.LastFMColorRed);
            this._embedAuthor = new EmbedAuthorBuilder();
            this._embedFooter = new EmbedFooterBuilder();
        }

        private async Task SendChartMessage(ChartSettings chartSettings, Embed embed)
        {
            await this._chartService.GenerateChartAsync(chartSettings);

            // Send chart memory stream, remove when finished
            using (var memory = await GlobalVars.GetChartStreamAsync(chartSettings.DiscordUser.Id))
            {
                await this.Context.Channel.SendFileAsync(memory, "chart.png", null, false, embed);
            }

            lock (GlobalVars.charts.SyncRoot)
            {
                // @TODO: remove only once in a while to keep it cached
                GlobalVars.charts.Remove(GlobalVars.GetChartFileName(chartSettings.DiscordUser.Id));
            }
        }

        [Command("chart", RunMode = RunMode.Async)]
        [Summary("Generates a chart based on a user's parameters.")]
        [Alias("c")]
        public async Task ChartAsync(string chartSize = "3x3", string time = "weekly", params string[] otherSettings)
        {
            var prfx = this._prefixService.GetPrefix(this.Context.Guild.Id) ?? ConfigData.Data.CommandPrefix;
            if (chartSize == "help")
            {
                await ReplyAsync($"{prfx}chart '2x2-8x8' 'weekly/monthly/yearly/overall' \n" +
                                 "Optional extra settings: 'notitles', 'nt', 'skipemptyimages', 's'\n" +
                                 "Size and time period are always required before any other parameters.");
                return;
            }

            var msg = this.Context.Message as SocketUserMessage;
            if (StackCooldownTarget.Contains(this.Context.Message.Author))
            {
                if (StackCooldownTimer[StackCooldownTarget.IndexOf(msg.Author)].AddSeconds(5) >= DateTimeOffset.Now)
                {
                    var secondsLeft = (int)(StackCooldownTimer[
                                                    StackCooldownTarget.IndexOf(this.Context.Message.Author as SocketGuildUser)]
                                                .AddSeconds(6) - DateTimeOffset.Now).TotalSeconds;
                    if (secondsLeft <= 2)
                    {
                        var secondString = secondsLeft == 1 ? "second" : "seconds";
                        await ReplyAsync($"Please wait {secondsLeft} {secondString} before generating a chart again.");
                    }

                    return;
                }

                StackCooldownTimer[StackCooldownTarget.IndexOf(msg.Author)] = DateTimeOffset.Now;
            }
            else
            {
                StackCooldownTarget.Add(msg.Author);
                StackCooldownTimer.Add(DateTimeOffset.Now);
            }

            var userSettings = await this._userService.GetUserSettingsAsync(this.Context.User);

            if (userSettings?.UserNameLastFM == null)
            {
                await UsernameNotSetErrorResponseAsync();
                return;
            }

            var lastFMUserName = userSettings.UserNameLastFM;
            var self = true;

            if (!this._guildService.CheckIfDM(this.Context))
            {
                var perms = await this._guildService.CheckSufficientPermissionsAsync(this.Context);
                if (!perms.AttachFiles)
                {
                    await ReplyAsync(
                        "I'm missing the 'Attach files' permission in this server, so I can't post a chart.");
                    return;
                }
            }

            // @TODO: change to intparse or the likes
            try
            {
                int albumCount;
                int chartRows;

                switch (chartSize)
                {
                    case "2x2":
                        albumCount = 4;
                        chartRows = 2;
                        break;
                    case "3x3":
                        albumCount = 9;
                        chartRows = 3;
                        break;
                    case "4x4":
                        albumCount = 16;
                        chartRows = 4;
                        break;
                    case "5x5":
                        albumCount = 25;
                        chartRows = 5;
                        break;
                    case "6x6":
                        albumCount = 36;
                        chartRows = 6;
                        break;
                    case "7x7":
                        albumCount = 49;
                        chartRows = 7;
                        break;
                    case "8x8":
                        albumCount = 64;
                        chartRows = 8;
                        break;
                    default:
                        await ReplyAsync("Your chart's size isn't valid. Sizes supported: 2x2-8x8. \n" +
                                         $"Example: `{prfx}fmchart 5x5 monthly notitles skipemptyimages`. For more info, use `{prfx}chart help`");
                        return;
                }

                _ = this.Context.Channel.TriggerTypingAsync();

                // Generating image
                var timespan = this._lastFmService.StringToLastStatsTimeSpan(time);

                var chartSettings = new ChartSettings(chartRows, 0, this.Context.User);

                chartSettings = this._chartService.SetExtraSettings(chartSettings, otherSettings);
                chartSettings.ImagesNeeded = albumCount;

                var extraAlbums = 0;
                if (chartSettings.SkipArtistsWithoutImage)
                {
                    extraAlbums = chartRows * 2 + (chartRows > 5 ? 8 : 2);
                }

                albumCount += extraAlbums;

                var albums = await this._lastFmService.GetTopAlbumsAsync(lastFMUserName, timespan, albumCount);

                if (albums.Count() < albumCount)
                {
                    var reply =
                        $"User hasn't listened to enough albums ({albums.Count()} of required {albumCount}) for a chart this size. " +
                        "Please try a smaller chart or a bigger time period (weekly/monthly/yearly/overall)'.";

                    if (chartSettings.SkipArtistsWithoutImage)
                    {
                        reply += "\n\n" +
                                 $"Note that {extraAlbums} extra albums are required because you are skipping albums without an image.";
                    }

                    await ReplyAsync(reply);
                    return;
                }

                chartSettings.Albums = albums;

                await this._userService.ResetChartTimerAsync(userSettings);

                string chartDescription;
                string datePreset;
                if (time.Equals("weekly") || time.Equals("week") || time.Equals("w"))
                {
                    chartDescription = chartSize + " Weekly Chart";
                    datePreset = "LAST_7_DAYS";
                }
                else if (time.Equals("monthly") || time.Equals("month") || time.Equals("m"))
                {
                    chartDescription = chartSize + " Monthly Chart";
                    datePreset = "LAST_30_DAYS";
                }
                else if (time.Equals("yearly") || time.Equals("year") || time.Equals("y"))
                {
                    chartDescription = chartSize + " Yearly Chart";
                    datePreset = "LAST_365_DAYS";
                }
                else if (time.Equals("overall") || time.Equals("alltime") || time.Equals("o") || time.Equals("at") ||
                         time.Equals("a"))
                {
                    chartDescription = chartSize + " Overall Chart";
                    datePreset = "ALL";
                }
                else
                {
                    chartDescription = chartSize + " Chart";
                    datePreset = "LAST_7_DAYS";
                }

                if (self)
                {
                    this._embedAuthor.WithName(chartDescription + " for " +
                                               await this._userService.GetUserTitleAsync(this.Context));
                }
                else
                {
                    this._embedAuthor.WithName(
                        $"{chartDescription} for {lastFMUserName}, requested by {await this._userService.GetUserTitleAsync(this.Context)}");
                }

                this._embedAuthor.WithIconUrl(this.Context.User.GetAvatarUrl());
                this._embedAuthor.WithUrl(
                    $"{Constants.LastFMUserUrl}{lastFMUserName}/library/albums?date_preset={datePreset}");

                this._embed.WithAuthor(this._embedAuthor);
                var userInfo = await this._lastFmService.GetUserInfoAsync(lastFMUserName);

                var playCount = userInfo.Content.Playcount;

                this._embedFooter.Text = $"{lastFMUserName} has {playCount} scrobbles.";

                if (chartSettings.SkipArtistsWithoutImage)
                {
                    this._embed.AddField("Skip albums without images?", "Enabled");
                }

                if (!chartSettings.TitlesEnabled)
                {
                    this._embed.AddField("Titles enabled?", "False");
                }

                this._embed.WithFooter(this._embedFooter);

                await SendChartMessage(chartSettings, this._embed.Build());

                this._logger.LogCommandUsed(this.Context.Guild?.Id, this.Context.Channel.Id, this.Context.User.Id,
                    this.Context.Message.Content);
            }
            catch (Exception e)
            {
                this._logger.LogError(e.Message, this.Context.Message.Content, this.Context.User.Username,
                    this.Context.Guild?.Name, this.Context.Guild?.Id);
                await ReplyAsync(
                    "Sorry, but I was unable to generate a FMChart due to an internal error. Make sure you have scrobbles and Last.FM isn't having issues, and try again later.");
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
