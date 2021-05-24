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
using FMBot.Bot.Services.Guild;
using FMBot.Domain;
using FMBot.Domain.Models;
using FMBot.LastFM.Repositories;
using SkiaSharp;

namespace FMBot.Bot.Commands.LastFM
{
    [Name("Charts")]
    public class ChartCommands : ModuleBase
    {
        private readonly GuildService _guildService;
        private readonly IChartService _chartService;
        private readonly IPrefixService _prefixService;
        private readonly LastFmRepository _lastFmRepository;
        private readonly SettingService _settingService;
        private readonly SupporterService _supporterService;
        private readonly UserService _userService;

        private readonly EmbedAuthorBuilder _embedAuthor;
        private readonly EmbedBuilder _embed;
        private readonly EmbedFooterBuilder _embedFooter;

        private static readonly List<DateTimeOffset> StackCooldownTimer = new();
        private static readonly List<SocketUser> StackCooldownTarget = new();

        public ChartCommands(
                GuildService guildService,
                IChartService chartService,
                IPrefixService prefixService,
                LastFmRepository lastFmRepository,
                SettingService settingService,
                SupporterService supporterService,
                UserService userService
            )
        {
            this._chartService = chartService;
            this._guildService = guildService;
            this._lastFmRepository = lastFmRepository;
            this._prefixService = prefixService;
            this._settingService = settingService;
            this._supporterService = supporterService;
            this._userService = userService;

            this._embedAuthor = new EmbedAuthorBuilder();
            this._embed = new EmbedBuilder()
                .WithColor(DiscordConstants.LastFmColorRed);
            this._embedFooter = new EmbedFooterBuilder();
        }

        [Command("chart", RunMode = RunMode.Async)]
        [Summary("Generates a an album chart.")]
        [Options(
            Constants.CompactTimePeriodList,
            "Disable titles: `notitles` / `nt`",
            "Skip albums with no image: `skipemptyimages` / `s`",
            "Size: `2x2`, `3x3` up to `10x10`",
            Constants.UserMentionExample)]
        [Examples("c", "c q 8x8 nt s", "chart 8x8 quarterly notitles skip", "c 10x10 alltime notitles skip", "c @user 7x7 yearly")]
        [Alias("c")]
        [UsernameSetRequired]
        public async Task ChartAsync(params string[] otherSettings)
        {
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id) ?? ConfigData.Data.Bot.Prefix;

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

            var optionsAsString = "";
            if (otherSettings != null && otherSettings.Any())
            {
                optionsAsString = string.Join(" ", otherSettings);
            }
            var userSettings = await this._settingService.GetUser(optionsAsString, user, this.Context);

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

                var chartSettings = new ChartSettings(this.Context.User);

                chartSettings = this._chartService.SetSettings(chartSettings, otherSettings, this.Context);

                var extraAlbums = 0;
                if (chartSettings.SkipArtistsWithoutImage)
                {
                    extraAlbums = chartSettings.Height * 2 + (chartSettings.Height > 5 ? 8 : 2);
                }

                var imagesToRequest = chartSettings.ImagesNeeded + extraAlbums;

                var albums = await this._lastFmRepository.GetTopAlbumsAsync(userSettings.UserNameLastFm, chartSettings.TimeSpan, imagesToRequest);

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

                var embedAuthorDescription = "";
                if (!userSettings.DifferentUser)
                {
                    embedAuthorDescription = $"{chartSettings.Width}x{chartSettings.Height} {chartSettings.TimespanString} for " +
                                             await this._userService.GetUserTitleAsync(this.Context);
                }
                else
                {
                    embedAuthorDescription =
                        $"{chartSettings.Width}x{chartSettings.Height} {chartSettings.TimespanString} for {userSettings.UserNameLastFm}, requested by {await this._userService.GetUserTitleAsync(this.Context)}";
                }

                this._embedAuthor.WithName(embedAuthorDescription);
                this._embedAuthor.WithIconUrl(this.Context.User.GetAvatarUrl());
                this._embedAuthor.WithUrl(
                    $"{Constants.LastFMUserUrl}{userSettings.UserNameLastFm}/library/albums?{chartSettings.TimespanUrlString}");

                var embedDescription = "";

                this._embed.WithAuthor(this._embedAuthor);

                if (!userSettings.DifferentUser)
                {
                    this._embedFooter.Text = $"{userSettings.UserNameLastFm} has {user.TotalPlaycount} scrobbles.";
                    this._embed.WithFooter(this._embedFooter);
                }

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
                if (chartSettings.RainbowSortingEnabled)
                {
                    embedDescription += "- Secret rainbow option enabled! (Not perfect but hey, it somewhat exists)\n";
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
                        $"*This chart was brought to you by .fmbot supporter {supporter}. Also want to support .fmbot? Check out `{prfx}donate`.*\n";
                }


                var nsfwAllowed = this.Context.Guild == null || ((SocketTextChannel) this.Context.Channel).IsNsfw;
                var chart = await this._chartService.GenerateChartAsync(chartSettings, nsfwAllowed);

                if (chartSettings.CensoredAlbums.HasValue && chartSettings.CensoredAlbums > 0)
                {
                    if (nsfwAllowed)
                    {
                        embedDescription +=
                            $"{chartSettings.CensoredAlbums.Value} album(s) filtered due to images that are not allowed to be posted on Discord.\n";
                    }
                    else
                    {
                        embedDescription +=
                            $"{chartSettings.CensoredAlbums.Value} album(s) filtered due to nsfw images.\n";
                    }
                    
                }

                this._embed.WithDescription(embedDescription);

                var encoded = chart.Encode(SKEncodedImageFormat.Png, 100);
                var stream = encoded.AsStream();

                await this.Context.Channel.SendFileAsync(
                    stream,
                    $"chart-{chartSettings.Width}w-{chartSettings.Height}h-{chartSettings.TimeSpan}-{userSettings.UserNameLastFm}.png",
                    embed: this._embed.Build());

                this.Context.LogCommandUsed();
            }
            catch (Exception e)
            {
                this.Context.LogCommandException(e);
                await ReplyAsync(
                    "Sorry, but I was unable to generate a FMChart due to an internal error. Make sure you have scrobbles and Last.fm isn't having issues, and try again later.");
            }
        }
    }
}
