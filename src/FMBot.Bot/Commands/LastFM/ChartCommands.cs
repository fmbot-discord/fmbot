using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using FMBot.Bot.Attributes;
using FMBot.Bot.Configurations;
using FMBot.Bot.Extensions;
using FMBot.Bot.Interfaces;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Domain;
using FMBot.Domain.Models;
using FMBot.LastFM.Services;
using IF.Lastfm.Core.Api.Helpers;
using IF.Lastfm.Core.Objects; 
using SkiaSharp;

namespace FMBot.Bot.Commands.LastFM
{
    public class ChartCommands : ModuleBase
    {
        private static readonly List<DateTimeOffset> StackCooldownTimer = new List<DateTimeOffset>();
        private static readonly List<SocketUser> StackCooldownTarget = new List<SocketUser>();
        private readonly IChartService _chartService;
        private readonly EmbedBuilder _embed;
        private readonly EmbedAuthorBuilder _embedAuthor;
        private readonly EmbedFooterBuilder _embedFooter;
        private readonly GuildService _guildService;
        private readonly LastFMService _lastFmService;
        private readonly SupporterService _supporterService;
        private readonly IPrefixService _prefixService;

        private readonly UserService _userService;

        public ChartCommands(
            IPrefixService prefixService,
            IChartService chartService,
            GuildService guildService,
            UserService userService,
            LastFMService lastFmService,
            SupporterService supporterService)
        {
            this._prefixService = prefixService;
            this._chartService = chartService;
            this._guildService = guildService;
            this._userService = userService;
            this._lastFmService = lastFmService;
            this._supporterService = supporterService;
            this._embed = new EmbedBuilder()
                .WithColor(DiscordConstants.LastFMColorRed);
            this._embedAuthor = new EmbedAuthorBuilder();
            this._embedFooter = new EmbedFooterBuilder();
        }

        [Command("chart", RunMode = RunMode.Async)]
        [Summary("Generates a chart based on a user's parameters.")]
        [Alias("c")]
        [UsernameSetRequired]
        public async Task ChartAsync(params string[] otherSettings)
        {
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id) ?? ConfigData.Data.Bot.Prefix;
            if (otherSettings.Any() && otherSettings.First() == "help")
            {
                await ReplyAsync($"{prfx}chart '2x2-10x10' '{Constants.CompactTimePeriodList}' \n" +
                                 "Optional extra settings: 'notitles', 'nt', 'skipemptyimages', 's'\n" +
                                 "Options can be used in any order..");
                this.Context.LogCommandUsed(CommandResponse.Help);
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
                        this.Context.LogCommandUsed(CommandResponse.Cooldown);
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

            var user = await this._userService.GetUserSettingsAsync(this.Context.User);

            var userSettings = await SettingService.GetUser(otherSettings, user.UserNameLastFM, this.Context);

            if (!this._guildService.CheckIfDM(this.Context))
            {
                var perms = await this._guildService.CheckSufficientPermissionsAsync(this.Context);
                if (!perms.AttachFiles)
                {
                    await ReplyAsync(
                        "I'm missing the 'Attach files' permission in this server, so I can't post a chart.");
                    this.Context.LogCommandUsed(CommandResponse.NoPermission);
                    return;
                }
            }

            try
            {
                _ = this.Context.Channel.TriggerTypingAsync();

                // Generating image
                var chartSettings = new ChartSettings(this.Context.User);

                chartSettings = this._chartService.SetSettings(chartSettings, otherSettings);

                var extraAlbums = 0;
                if (chartSettings.SkipArtistsWithoutImage)
                {
                    extraAlbums = chartSettings.Height * 2 + (chartSettings.Height > 5 ? 8 : 2);
                }

                var imagesToRequest = chartSettings.ImagesNeeded + extraAlbums;

                var albums = await this._lastFmService.GetTopAlbumsAsync(userSettings.UserNameLastFm, chartSettings.TimeSpan, imagesToRequest);

                if (albums.Count() < chartSettings.ImagesNeeded)
                {
                    var reply =
                        $"User hasn't listened to enough albums ({albums.Count()} of required {chartSettings.ImagesNeeded}) for a chart this size. " +
                        $"Please try a smaller chart or a bigger time period ({Constants.CompactTimePeriodList})'.";

                    if (chartSettings.SkipArtistsWithoutImage)
                    {
                        reply += "\n\n" +
                                 $"Note that {extraAlbums} extra albums are required because you are skipping albums without an image.";
                    }

                    await ReplyAsync(reply);
                    this.Context.LogCommandUsed(CommandResponse.WrongInput);
                    return;
                }

                chartSettings.Albums = albums.Content.ToList();

                if (!userSettings.DifferentUser)
                {
                    this._embedAuthor.WithName($"{chartSettings.Width}x{chartSettings.Height} {chartSettings.TimespanString} for " +
                                               await this._userService.GetUserTitleAsync(this.Context));
                }
                else
                {
                    this._embedAuthor.WithName(
                        $"{chartSettings.Width}x{chartSettings.Height} {chartSettings.TimespanString} for {userSettings.UserNameLastFm}, requested by {await this._userService.GetUserTitleAsync(this.Context)}");
                }

                this._embedAuthor.WithIconUrl(this.Context.User.GetAvatarUrl());
                this._embedAuthor.WithUrl(
                    $"{Constants.LastFMUserUrl}{userSettings.UserNameLastFm}/library/albums?{chartSettings.TimespanUrlString}");

                this._embed.WithAuthor(this._embedAuthor);
                var userInfo = await this._lastFmService.GetUserInfoAsync(userSettings.UserNameLastFm);

                var playCount = userInfo.Content.Playcount;

                this._embedFooter.Text = $"{userSettings.UserNameLastFm} has {playCount} scrobbles.";

                string embedDescription = "";
                if (chartSettings.CustomOptionsEnabled)
                {
                    embedDescription += "Chart options:\n";
                }
                if (chartSettings.SkipArtistsWithoutImage)
                {
                    embedDescription += "- Albums without images skipped\n";
                }
                if (chartSettings.TitleSetting == TitleSetting.TitlesDisabled)
                {
                    embedDescription += "- Album titles disabled\n";
                }
                if (chartSettings.TitleSetting == TitleSetting.ClassicTitles)
                {
                    embedDescription += "- Classic titles enabled\n";
                }

                var rnd = new Random();
                if (chartSettings.ImagesNeeded == 1 && rnd.Next(0, 3) == 1)
                {
                    embedDescription += "*Linus Tech Tip: If you want the cover of the album you're currently listening to, use `.fmcover` or `.fmco`.*\n";
                }

                if (chartSettings.UsePlays)
                {
                    embedDescription +=
                        "⚠️ Sorry, but using time periods that use your play history isn't supported for this command.\n";
                }

                var supporter = await this._supporterService.GetRandomSupporter(this.Context.Guild);
                if (!string.IsNullOrEmpty(supporter))
                {
                    embedDescription +=
                        $"This chart was brought to you by .fmbot supporter {supporter}.\n";
                }

                this._embed.WithFooter(this._embedFooter);

                var chart = await this._chartService.GenerateChartAsync(chartSettings);

                if (chartSettings.CensoredAlbums.HasValue && chartSettings.CensoredAlbums > 0)
                {
                    embedDescription +=
                        $"{chartSettings.CensoredAlbums.Value} album(s) filtered due to nsfw images.\n";
                }

                this._embed.WithDescription(embedDescription);

                var encoded = chart.Encode(SKEncodedImageFormat.Png, 100);
                var stream = encoded.AsStream();

                await this.Context.Channel.SendFileAsync(
                    stream,
                    $"chart-{chartSettings.Width}w-{chartSettings.Height}h-{chartSettings.TimeSpan.ToString()}-{userSettings.UserNameLastFm}.png",
                    null,
                    false,
                    this._embed.Build());

                this.Context.LogCommandUsed();
            }
            catch (Exception e)
            {
                this.Context.LogCommandException(e);
                await ReplyAsync(
                    "Sorry, but I was unable to generate a FMChart due to an internal error. Make sure you have scrobbles and Last.FM isn't having issues, and try again later.");
            }
        }
    }
}
