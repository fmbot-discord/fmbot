using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Discord.Commands;
using Discord.WebSocket;
using FMBot.Bot.Attributes;
using FMBot.Bot.Extensions;
using FMBot.Bot.Interfaces;
using FMBot.Bot.Models;
using FMBot.Bot.Services;
using FMBot.Bot.Services.Guild;
using FMBot.Bot.Services.ThirdParty;
using FMBot.Domain;
using FMBot.Domain.Models;
using FMBot.LastFM.Repositories;
using Microsoft.Extensions.Options;
using SkiaSharp;

namespace FMBot.Bot.Commands.LastFM
{
    [Name("Charts")]
    public class ChartCommands : BaseCommandModule
    {
        private readonly AlbumService _albumService;
        private readonly ArtistsService _artistService;
        private readonly GuildService _guildService;
        private readonly ChartService _chartService;
        private readonly IPrefixService _prefixService;
        private readonly LastFmRepository _lastFmRepository;
        private readonly SettingService _settingService;
        private readonly SupporterService _supporterService;
        private readonly UserService _userService;
        private readonly SpotifyService _spotifyService;

        private static readonly List<DateTimeOffset> StackCooldownTimer = new();
        private static readonly List<SocketUser> StackCooldownTarget = new();

        public ChartCommands(
                GuildService guildService,
                ChartService chartService,
                IPrefixService prefixService,
                LastFmRepository lastFmRepository,
                SettingService settingService,
                SupporterService supporterService,
                UserService userService,
                AlbumService albumService,
                ArtistsService artistService,
                IOptions<BotSettings> botSettings,
                SpotifyService spotifyService) : base(botSettings)
        {
            this._chartService = chartService;
            this._guildService = guildService;
            this._lastFmRepository = lastFmRepository;
            this._prefixService = prefixService;
            this._settingService = settingService;
            this._supporterService = supporterService;
            this._userService = userService;
            this._albumService = albumService;
            this._artistService = artistService;
            this._spotifyService = spotifyService;
        }

        [Command("chart", RunMode = RunMode.Async)]
        [Summary("Generates an album image chart.")]
        [Options(
            Constants.CompactTimePeriodList,
            "Disable titles: `notitles` / `nt`",
            "Skip albums with no image: `skipemptyimages` / `s`",
            "Size: WidthxHeight - `2x2`, `3x3`, `4x5` up to `10x10`",
            Constants.UserMentionExample)]
        [Examples("c", "c q 8x8 nt s", "chart 8x8 quarterly notitles skip", "c 10x10 alltime notitles skip", "c @user 7x7 yearly")]
        [Alias("c", "aoty")]
        [UsernameSetRequired]
        [CommandCategories(CommandCategory.Charts, CommandCategory.Albums)]
        public async Task ChartAsync(params string[] otherSettings)
        {
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);

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
                var perms = await GuildService.GetGuildPermissionsAsync(this.Context);
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

                var chartSettings = new ChartSettings(this.Context.User)
                {
                    ArtistChart = false
                };

                chartSettings = this._chartService.SetSettings(chartSettings, otherSettings, this.Context);

                if (chartSettings.ImagesNeeded > 100)
                {
                    const string reply = $"You can't create a chart with more then 100 images (10x10).\n" +
                                         $"Please try a smaller size.";

                    await ReplyAsync(reply);
                    this.Context.LogCommandUsed(CommandResponse.WrongInput);
                    return;
                }

                var extraAlbums = 0;
                if (chartSettings.SkipWithoutImage)
                {
                    extraAlbums = chartSettings.Height * 2 + (chartSettings.Height > 5 ? 8 : 2);
                }

                var imagesToRequest = chartSettings.ImagesNeeded + extraAlbums;

                var albums = await this._lastFmRepository.GetTopAlbumsAsync(userSettings.UserNameLastFm, chartSettings.TimeSettings, imagesToRequest);

                if (albums.Content.TopAlbums == null || albums.Content.TopAlbums.Count < chartSettings.ImagesNeeded)
                {
                    var count = albums.Content.TopAlbums?.Count ?? 0;

                    var reply =
                        $"User hasn't listened to enough albums ({count} of required {chartSettings.ImagesNeeded}) for a chart this size. \n" +
                        $"Please try a smaller chart or a bigger time period ({Constants.CompactTimePeriodList}).";

                    if (chartSettings.SkipWithoutImage)
                    {
                        reply += "\n\n" +
                                 $"Note that {extraAlbums} extra albums are required because you are skipping albums without an image.";
                    }

                    await ReplyAsync(reply);
                    this.Context.LogCommandUsed(CommandResponse.WrongInput);
                    return;
                }

                var topAlbums = albums.Content.TopAlbums;

                topAlbums = await this._albumService.FillMissingAlbumCovers(topAlbums);

                var albumsWithoutImage = topAlbums.Where(f => f.AlbumCoverUrl == null).ToList();

                var amountToFetch = albumsWithoutImage.Count > 3 ? 3 : albumsWithoutImage.Count;
                for (var i = 0; i < amountToFetch; i++)
                {
                    var albumWithoutImage = albumsWithoutImage[i];
                    var albumCall = await this._lastFmRepository.GetAlbumInfoAsync(albumWithoutImage.ArtistName, albumWithoutImage.AlbumName, userSettings.UserNameLastFm, userSettings.DifferentUser ? null : userSettings.SessionKeyLastFm);
                    if (albumCall.Success && albumCall.Content?.AlbumUrl != null)
                    {
                        var spotifyArtistImage = await this._spotifyService.GetOrStoreSpotifyAlbumAsync(albumCall.Content);
                        if (spotifyArtistImage?.SpotifyImageUrl != null)
                        {
                            var index = topAlbums.FindIndex(f => f.ArtistName == albumWithoutImage.ArtistName &&
                                                                 f.AlbumName == albumWithoutImage.AlbumName);
                            topAlbums[index].AlbumCoverUrl = spotifyArtistImage.SpotifyImageUrl;
                        }
                    }
                }

                chartSettings.Albums = topAlbums;

                var embedAuthorDescription = "";
                if (!userSettings.DifferentUser)
                {
                    embedAuthorDescription = $"{chartSettings.Width}x{chartSettings.Height} {chartSettings.TimespanString} Chart for " +
                                             await this._userService.GetUserTitleAsync(this.Context);
                }
                else
                {
                    embedAuthorDescription =
                        $"{chartSettings.Width}x{chartSettings.Height} {chartSettings.TimespanString} Chart for {userSettings.UserNameLastFm}, requested by {await this._userService.GetUserTitleAsync(this.Context)}";
                }

                this._embedAuthor.WithName(embedAuthorDescription);
                this._embedAuthor.WithIconUrl(this.Context.User.GetAvatarUrl());
                this._embedAuthor.WithUrl(
                    $"{Constants.LastFMUserUrl}{userSettings.UserNameLastFm}/library/albums?{chartSettings.TimespanUrlString}");

                var embedDescription = "";

                this._embed.WithAuthor(this._embedAuthor);

                if (!userSettings.DifferentUser)
                {
                    this._embedFooter.Text = $"{userSettings.UserNameLastFm} has {user.TotalPlaycount} scrobbles";
                    this._embed.WithFooter(this._embedFooter);
                }

                var supporter = await this._supporterService.GetRandomSupporter(this.Context.Guild, user.UserType);
                embedDescription += ChartService.AddSettingsToDescription(chartSettings, embedDescription, supporter, prfx);

                var nsfwAllowed = this.Context.Guild == null || ((SocketTextChannel)this.Context.Channel).IsNsfw;
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
                            $"{chartSettings.CensoredAlbums.Value} album(s) filtered due to nsfw images. Use this command in an nsfw channel to disable this.\n";
                    }
                }

                this._embed.WithDescription(embedDescription);

                var encoded = chart.Encode(SKEncodedImageFormat.Png, 100);
                var stream = encoded.AsStream();

                await this.Context.Channel.SendFileAsync(
                    stream,
                    $"chart-{chartSettings.Width}w-{chartSettings.Height}h-{chartSettings.TimeSettings.TimePeriod}-{userSettings.UserNameLastFm}.png",
                    embed: this._embed.Build());
                await stream.DisposeAsync();

                this.Context.LogCommandUsed();
            }
            catch (Exception e)
            {
                this.Context.LogCommandException(e);
                await ReplyAsync(
                    "Sorry, but I was unable to generate a FMChart due to an internal error. Make sure you have scrobbles and Last.fm isn't having issues, and try again later.");
            }
        }

        [Command("artistchart", RunMode = RunMode.Async)]
        [Summary("Generates an artist image chart.")]
        [Options(
            Constants.CompactTimePeriodList,
            "Disable titles: `notitles` / `nt`",
            "Skip albums with no image: `skipemptyimages` / `s`",
            "Size: WidthxHeight - `2x2`, `3x3`, `4x5` up to `10x10`",
            Constants.UserMentionExample)]
        [Examples("ac", "ac q 8x8 nt s", "artistchart 8x8 quarterly notitles skip", "ac 10x10 alltime notitles skip", "ac @user 7x7 yearly")]
        [Alias("ac", "top")]
        [UsernameSetRequired]
        [CommandCategories(CommandCategory.Charts, CommandCategory.Artists)]
        public async Task ArtistChartAsync(params string[] otherSettings)
        {
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);

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
                var perms = await GuildService.GetGuildPermissionsAsync(this.Context);
                if (!perms.AttachFiles)
                {
                    await ReplyAsync(
                        "I'm missing the 'Attach files' permission in this server, so I can't post an artist chart.");
                    this.Context.LogCommandUsed(CommandResponse.NoPermission);
                    return;
                }
            }

            try
            {
                _ = this.Context.Channel.TriggerTypingAsync();

                var chartSettings = new ChartSettings(this.Context.User) {ArtistChart = true};

                chartSettings = this._chartService.SetSettings(chartSettings, otherSettings, this.Context);

                var extraArtists = 0;
                if (chartSettings.SkipWithoutImage)
                {
                    extraArtists = chartSettings.Height * 2 + (chartSettings.Height > 5 ? 8 : 2);
                }

                var imagesToRequest = chartSettings.ImagesNeeded + extraArtists;

                var artists = await this._lastFmRepository.GetTopArtistsAsync(userSettings.UserNameLastFm, chartSettings.TimeSettings, imagesToRequest);

                if (artists.Content.TopArtists == null || artists.Content.TopArtists.Count < chartSettings.ImagesNeeded)
                {
                    var count = artists.Content.TopArtists?.Count ?? 0;

                    var reply =
                        $"User hasn't listened to enough artists ({count} of required {chartSettings.ImagesNeeded}) for a chart this size. \n" +
                        $"Please try a smaller chart or a bigger time period ({Constants.CompactTimePeriodList}).";

                    if (chartSettings.SkipWithoutImage)
                    {
                        reply += "\n\n" +
                                 $"Note that {extraArtists} extra albums are required because you are skipping artists without an image.";
                    }

                    await ReplyAsync(reply);
                    this.Context.LogCommandUsed(CommandResponse.WrongInput);
                    return;
                }

                var topArtists = artists.Content.TopArtists;

                topArtists = await this._artistService.FillArtistImages(topArtists);

                var artistsWithoutImages = topArtists.Where(w => w.ArtistImageUrl == null).ToList();

                var amountToFetch = artistsWithoutImages.Count > 3 ? 3 : artistsWithoutImages.Count;
                for (int i = 0; i < amountToFetch; i++)
                {
                    var artistWithoutImage = artistsWithoutImages[i];

                    var artistCall = await this._lastFmRepository.GetArtistInfoAsync(artistWithoutImage.ArtistName, userSettings.UserNameLastFm, userSettings.DifferentUser ? null : userSettings.SessionKeyLastFm);
                    if (artistCall.Success && artistCall.Content?.ArtistUrl != null)
                    {
                        var spotifyArtistImage = await this._spotifyService.GetOrStoreArtistAsync(artistCall.Content);
                        if (spotifyArtistImage != null)
                        {
                            var index = topArtists.FindIndex(f => f.ArtistName == artistWithoutImage.ArtistName);
                            topArtists[index].ArtistImageUrl = spotifyArtistImage.SpotifyImageUrl;
                        }
                    }
                }

                chartSettings.Artists = topArtists;

                var embedAuthorDescription = "";
                if (!userSettings.DifferentUser)
                {
                    embedAuthorDescription = $"{chartSettings.Width}x{chartSettings.Height} {chartSettings.TimespanString} Artist Chart for " +
                                             await this._userService.GetUserTitleAsync(this.Context);
                }
                else
                {
                    embedAuthorDescription =
                        $"{chartSettings.Width}x{chartSettings.Height} {chartSettings.TimespanString} Artist Chart for {userSettings.UserNameLastFm}, requested by {await this._userService.GetUserTitleAsync(this.Context)}";
                }

                this._embedAuthor.WithName(embedAuthorDescription);
                this._embedAuthor.WithIconUrl(this.Context.User.GetAvatarUrl());
                this._embedAuthor.WithUrl(
                    $"{Constants.LastFMUserUrl}{userSettings.UserNameLastFm}/library/artists?{chartSettings.TimespanUrlString}");

                var embedDescription = "";

                this._embed.WithAuthor(this._embedAuthor);

                var footer = new StringBuilder();

                footer.AppendLine("Image source: Spotify | Use 'skip' to skip artists without images");

                if (!userSettings.DifferentUser)
                {
                    footer.AppendLine($"{userSettings.UserNameLastFm} has {user.TotalPlaycount} scrobbles");
                }

                this._embed.WithFooter(footer.ToString());

                var supporter = await this._supporterService.GetRandomSupporter(this.Context.Guild, user.UserType);
                embedDescription += ChartService.AddSettingsToDescription(chartSettings, embedDescription, supporter, prfx);

                var chart = await this._chartService.GenerateChartAsync(chartSettings, true);

                this._embed.WithDescription(embedDescription);

                var encoded = chart.Encode(SKEncodedImageFormat.Png, 100);
                var stream = encoded.AsStream();

                await this.Context.Channel.SendFileAsync(
                    stream,
                    $"chart-{chartSettings.Width}w-{chartSettings.Height}h-{chartSettings.TimeSettings.TimePeriod}-{userSettings.UserNameLastFm}.png",
                    embed: this._embed.Build());
                await stream.DisposeAsync();

                this.Context.LogCommandUsed();
            }
            catch (Exception e)
            {
                this.Context.LogCommandException(e);
                await ReplyAsync(
                    "Sorry, but I was unable to generate an artist chart due to an internal error. Make sure you have scrobbles and Last.fm isn't having issues, and try again later.");
            }
        }
    }
}
