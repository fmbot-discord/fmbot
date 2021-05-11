using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
using FMBot.Bot.Services.ThirdParty;
using FMBot.Bot.Services.WhoKnows;
using FMBot.Domain;
using FMBot.Domain.Models;
using FMBot.LastFM.Domain.Enums;
using FMBot.LastFM.Domain.Models;
using FMBot.LastFM.Domain.Types;
using FMBot.LastFM.Repositories;
using FMBot.Persistence.Domain.Models;
using Interactivity;
using Interactivity.Pagination;

namespace FMBot.Bot.Commands.LastFM
{
    [Name("Tracks")]
    public class TrackCommands : ModuleBase
    {
        private readonly GuildService _guildService;
        private readonly IIndexService _indexService;
        private readonly IPrefixService _prefixService;
        private readonly IUpdateService _updateService;
        private readonly LastFmRepository _lastFmRepository;
        private readonly PlayService _playService;
        private readonly SettingService _settingService;
        private readonly SpotifyService _spotifyService;
        private readonly UserService _userService;
        private readonly WhoKnowsTrackService _whoKnowsTrackService;
        private readonly WhoKnowsPlayService _whoKnowsPlayService;
        private readonly WhoKnowsService _whoKnowsService;

        private readonly EmbedAuthorBuilder _embedAuthor;
        private readonly EmbedBuilder _embed;
        private readonly EmbedFooterBuilder _embedFooter;
        private InteractivityService Interactivity { get; }


        private static readonly List<DateTimeOffset> StackCooldownTimer = new();
        private static readonly List<SocketUser> StackCooldownTarget = new();

        public TrackCommands(
                GuildService guildService,
                IIndexService indexService,
                IPrefixService prefixService,
                IUpdateService updateService,
                LastFmRepository lastFmRepository,
                PlayService playService,
                SettingService settingService,
                SpotifyService spotifyService,
                UserService userService,
                WhoKnowsTrackService whoKnowsTrackService,
                WhoKnowsPlayService whoKnowsPlayService,
                InteractivityService interactivity,
                WhoKnowsService whoKnowsService)
        {
            this._guildService = guildService;
            this._indexService = indexService;
            this._lastFmRepository = lastFmRepository;
            this._playService = playService;
            this._prefixService = prefixService;
            this._settingService = settingService;
            this._spotifyService = spotifyService;
            this._updateService = updateService;
            this._userService = userService;
            this._whoKnowsTrackService = whoKnowsTrackService;
            this._whoKnowsPlayService = whoKnowsPlayService;
            this.Interactivity = interactivity;
            this._whoKnowsService = whoKnowsService;

            this._embedAuthor = new EmbedAuthorBuilder();
            this._embed = new EmbedBuilder()
                .WithColor(DiscordConstants.LastFmColorRed);
            this._embedFooter = new EmbedFooterBuilder();
        }

        [Command("track", RunMode = RunMode.Async)]
        [Summary("Displays information about the track you're currently listening to or searching for.")]
        [Examples(
            "tr",
            "track",
            "track Depeche Mode Enjoy The Silence",
            "track Crystal Waters | Gypsy Woman (She's Homeless) - Radio Edit")]
        [Alias("tr", "ti", "ts", "trackinfo")]
        [UsernameSetRequired]
        public async Task TrackAsync([Remainder] string trackValues = null)
        {
            var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

            _ = this.Context.Channel.TriggerTypingAsync();

            var track = await this.SearchTrack(trackValues, contextUser.UserNameLastFM, contextUser.SessionKeyLastFm);
            if (track == null)
            {
                return;
            }

            var userTitle = await this._userService.GetUserTitleAsync(this.Context);

            this._embedAuthor.WithIconUrl(this.Context.User.GetAvatarUrl());
            this._embedAuthor.WithName($"Info about {track.ArtistName} - {track.TrackName} for {userTitle}");

            if (track.TrackUrl != null)
            {
                this._embed.WithUrl(track.TrackUrl);
            }

            this._embed.WithAuthor(this._embedAuthor);

            var spotifyTrack = await this._spotifyService.GetOrStoreTrackAsync(track);

            if (spotifyTrack != null && !string.IsNullOrEmpty(spotifyTrack.SpotifyId))
            {
                this._embed.AddField("Stats",
                    $"`{track.TotalListeners}` listeners\n" +
                    $"`{track.TotalPlaycount}` global {StringExtensions.GetPlaysString(track.TotalPlaycount)}\n" +
                    $"`{track.UserPlaycount}` {StringExtensions.GetPlaysString(track.UserPlaycount)} by you\n",
                    true);

                var trackLength = TimeSpan.FromMilliseconds(spotifyTrack.DurationMs.GetValueOrDefault());
                var formattedTrackLength = string.Format("{0}{1}:{2:D2}",
                    trackLength.Hours == 0 ? "" : $"{trackLength.Hours}:",
                    trackLength.Minutes,
                    trackLength.Seconds);

                var pitch = StringExtensions.KeyIntToPitchString(spotifyTrack.Key.GetValueOrDefault());
                var bpm = $"{spotifyTrack.Tempo.GetValueOrDefault():0.0}";

                this._embed.AddField("Info",
                    $"`{formattedTrackLength}` duration\n" +
                    $"`{pitch}` key\n" +
                    $"`{bpm}` bpm\n",
                    true);
            }
            else
            {
                this._embed.AddField("Listeners", track.TotalListeners, true);
                this._embed.AddField("Global playcount", track.TotalPlaycount, true);
                this._embed.AddField("Your playcount", track.UserPlaycount, true);
            }

            if (!string.IsNullOrWhiteSpace(track.Description))
            {
                this._embed.AddField("Summary", track.Description);
            }

            if (track.Tags != null && track.Tags.Any())
            {
                var tags = LastFmRepository.TagsToLinkedString(track.Tags);

                this._embed.AddField("Tags", tags);
            }

            await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());

            this.Context.LogCommandUsed();
        }

        [Command("trackplays", RunMode = RunMode.Async)]
        [Summary("Displays playcount for the track you're currently listening to or searching for.\n\n" +
                 "You can also mention another user to see their playcount.")]
        [Examples(
            "tp",
            "trackplays",
            "trackplays Mac DeMarco Here Comes The Cowboy",
            "tp lfm:fm-bot",
            "trackplays Cocteau Twins | Heaven or Las Vegas @user")]
        [Alias("tp", "trackplay", "tplays", "trackp", "track plays")]
        [UsernameSetRequired]
        public async Task TrackPlaysAsync([Remainder] string trackValues = null)
        {
            var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

            _ = this.Context.Channel.TriggerTypingAsync();

            var userSettings = await this._settingService.GetUser(trackValues, contextUser, this.Context);

            var track = await this.SearchTrack(userSettings.NewSearchValue, contextUser.UserNameLastFM, contextUser.SessionKeyLastFm, userSettings.UserNameLastFm);
            if (track == null)
            {
                return;
            }

            var reply =
                $"**{userSettings.DiscordUserName.FilterOutMentions()}{userSettings.UserType.UserTypeToIcon()}** has `{track.UserPlaycount}` {StringExtensions.GetPlaysString(track.UserPlaycount)} for **{track.TrackName.FilterOutMentions()}** " +
                $"by **{track.ArtistName.FilterOutMentions()}**";

            if (!userSettings.DifferentUser && contextUser.LastUpdated != null)
            {
                var playsLastWeek =
                    await this._playService.GetWeekTrackPlaycountAsync(userSettings.UserId, track.TrackName, track.ArtistName);
                if (playsLastWeek != 0)
                {
                    reply += $" (`{playsLastWeek}` last week)";
                }
            }

            await this.Context.Channel.SendMessageAsync(reply);
            this.Context.LogCommandUsed();
        }

        [Command("love", RunMode = RunMode.Async)]
        [Summary("Adds the track you're currently listening to or searching for to your last.fm loved tracks.")]
        [Examples("love", "l", "love Tame Impala Borderline")]
        [Alias("l", "heart", "favorite", "affection", "appreciation", "lust", "fuckyeah", "fukk")]
        [UserSessionRequired]
        public async Task LoveAsync([Remainder] string trackValues = null)
        {
            var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id) ?? ConfigData.Data.Bot.Prefix;

            if (!string.IsNullOrWhiteSpace(trackValues) && trackValues.ToLower() == "help")
            {
                this._embed.WithTitle($"{prfx}love");
                this._embed.WithDescription("Loves the track you're currently listening to or searching for on last.fm.");
                await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
                this.Context.LogCommandUsed(CommandResponse.Help);
                return;
            }

            _ = this.Context.Channel.TriggerTypingAsync();

            var track = await this.SearchTrack(trackValues, contextUser.UserNameLastFM, contextUser.SessionKeyLastFm);
            if (track == null)
            {
                return;
            }

            var userTitle = await this._userService.GetUserTitleAsync(this.Context);


            if (track.Loved)
            {
                this._embed.WithTitle($"❤️ Track already loved");
                this._embed.WithDescription(LastFmRepository.ResponseTrackToLinkedString(track));
            }
            else
            {
                var trackLoved = await this._lastFmRepository.LoveTrackAsync(contextUser, track.ArtistName, track.TrackName);

                if (trackLoved)
                {
                    this._embed.WithTitle($"❤️ Loved track for {userTitle}");
                    this._embed.WithDescription(LastFmRepository.ResponseTrackToLinkedString(track));
                }
                else
                {
                    await this.Context.Message.Channel.SendMessageAsync(
                        "Something went wrong while adding loved track.");
                    this.Context.LogCommandUsed(CommandResponse.Error);
                    return;
                }
            }

            await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
            this.Context.LogCommandUsed();
        }

        [Command("unlove", RunMode = RunMode.Async)]
        [Summary("Removes the track you're currently listening to or searching for from your last.fm loved tracks.")]
        [Examples("unlove", "ul", "unlove Lou Reed Brandenburg Gate")]
        [Alias("ul", "unheart", "hate", "fuck")]
        [UserSessionRequired]
        public async Task UnLoveAsync([Remainder] string trackValues = null)
        {
            var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id) ?? ConfigData.Data.Bot.Prefix;

            if (!string.IsNullOrWhiteSpace(trackValues) && trackValues.ToLower() == "help")
            {
                this._embed.WithTitle($"{prfx}unlove");
                this._embed.WithDescription("Unloves the track you're currently listening to or searching for on last.fm.");
                await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
                this.Context.LogCommandUsed(CommandResponse.Help);
                return;
            }

            _ = this.Context.Channel.TriggerTypingAsync();

            var track = await this.SearchTrack(trackValues, contextUser.UserNameLastFM, contextUser.SessionKeyLastFm);
            if (track == null)
            {
                return;
            }

            var userTitle = await this._userService.GetUserTitleAsync(this.Context);

            if (!track.Loved)
            {
                this._embed.WithTitle($"💔 Track wasn't loved");
                this._embed.WithDescription(LastFmRepository.ResponseTrackToLinkedString(track));
            }
            else
            {
                var trackLoved = await this._lastFmRepository.UnLoveTrackAsync(contextUser, track.ArtistName, track.TrackName);

                if (trackLoved)
                {
                    this._embed.WithTitle($"💔 Unloved track for {userTitle}");
                    this._embed.WithDescription(LastFmRepository.ResponseTrackToLinkedString(track));
                }
                else
                {
                    await this.Context.Message.Channel.SendMessageAsync(
                        "Something went wrong while unloving track.");
                    this.Context.LogCommandUsed(CommandResponse.Error);
                    return;
                }
            }

            await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
            this.Context.LogCommandUsed();
        }

        [Command("loved", RunMode = RunMode.Async)]
        [Summary("Displays a user's loved tracks.")]
        [Examples("loved", "lt", "lovedtracks lfm:fm-bot", "lovedtracks @user")]
        [Alias("lovedtracks", "lt")]
        [UserSessionRequired]
        [SupportsPagination]
        public async Task LovedAsync([Remainder] string extraOptions = null)
        {
            var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id) ?? ConfigData.Data.Bot.Prefix;

            _ = this.Context.Channel.TriggerTypingAsync();

            var userSettings = await this._settingService.GetUser(extraOptions, contextUser, this.Context);
            var amount = SettingService.GetAmount(extraOptions, 8, 12);


            var paginationEnabled = false;
            var pages = new List<PageBuilder>();
            var perms = await this._guildService.CheckSufficientPermissionsAsync(this.Context);
            if (perms.ManageMessages)
            {
                paginationEnabled = true;
            }

            try
            {
                string sessionKey = null;
                if (!userSettings.DifferentUser && !string.IsNullOrEmpty(contextUser.SessionKeyLastFm))
                {
                    sessionKey = contextUser.SessionKeyLastFm;
                }

                amount = paginationEnabled ? 100 : amount;

                var recentTracks = await this._lastFmRepository.GetLovedTracksAsync(userSettings.UserNameLastFm, amount, sessionKey: sessionKey);

                if (!recentTracks.Content.RecentTracks.Any())
                {
                    this._embed.WithDescription(
                        $"The Last.fm user `{userSettings.UserNameLastFm}` has no loved tracks yet! \n" +
                        $"Use `{prfx}love` to add tracks to your list.");
                    this.Context.LogCommandUsed(CommandResponse.NoScrobbles);
                    await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
                    return;
                }

                if (await ErrorService.RecentScrobbleCallFailedReply(recentTracks, userSettings.UserNameLastFm, this.Context))
                {
                    return;
                }

                var userTitle = await this._userService.GetUserTitleAsync(this.Context);
                var title = !userSettings.DifferentUser ? userTitle : $"{userSettings.UserNameLastFm}, requested by {userTitle}";
                this._embedAuthor.WithName($"Last loved tracks for {title}");

                this._embedAuthor.WithIconUrl(this.Context.User.GetAvatarUrl());
                this._embedAuthor.WithUrl(recentTracks.Content.UserRecentTracksUrl);

                string footer;
                var firstTrack = recentTracks.Content.RecentTracks[0];

                footer =
                    $"{userSettings.UserNameLastFm} has {recentTracks.Content.TotalAmount} loved tracks";

                if (!firstTrack.NowPlaying && firstTrack.TimePlayed.HasValue)
                {
                    footer += " | Last loved track:";
                    this._embed.WithTimestamp(firstTrack.TimePlayed.Value);
                }

                this._embedFooter.WithText(footer);

                var description = "";
                var resultAmount = recentTracks.Content.RecentTracks.Count;
                amount = resultAmount < amount ? resultAmount : amount;
                for (var i = 0; i < resultAmount; i++)
                {
                    var track = recentTracks.Content.RecentTracks[i];

                    if (i == 0)
                    {
                        if (track.AlbumCoverUrl != null)
                        {
                            this._embed.WithThumbnailUrl(track.AlbumCoverUrl);
                        }
                    }

                    var trackString = LastFmRepository.TrackToOneLinedLinkedString(track);

                    description += $"`{recentTracks.Content.TotalAmount - i}` - {trackString}\n";

                    var pageAmount = i + 1;
                    if (paginationEnabled && (pageAmount > 0 && pageAmount % 10 == 0 || pageAmount == amount))
                    {
                        pages.Add(new PageBuilder().WithDescription(description).WithAuthor(this._embedAuthor).WithFooter(footer));
                        description = "";
                    }
                }

                if (paginationEnabled)
                {
                    var paginator = new StaticPaginatorBuilder()
                        .WithPages(pages)
                        .WithFooter(PaginatorFooter.PageNumber)
                        .WithTimoutedEmbed(null)
                        .WithCancelledEmbed(null)
                        .WithEmotes(DiscordConstants.PaginationEmotes)
                        .Build();

                    _ = this.Interactivity.SendPaginatorAsync(paginator, this.Context.Channel, TimeSpan.FromSeconds(DiscordConstants.PaginationTimeoutInSeconds), runOnGateway: false);
                }
                else
                {
                    this._embed.WithAuthor(this._embedAuthor);
                    this._embed.WithDescription(description);
                    this._embed.WithFooter(this._embedFooter);

                    await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
                }

                this.Context.LogCommandUsed();
            }
            catch (Exception e)
            {
                this.Context.LogCommandException(e);
                await ReplyAsync(
                    "Unable to show your recent tracks on Last.fm due to an internal error. Please try again later or contact .fmbot support.");
            }
        }

        [Command("scrobble", RunMode = RunMode.Async)]
        [Summary("Scrobbles the track you're searching for. You can only scrobble tracks that already exist on Last.fm.")]
        [Examples("scrobble", "sb stronger Kanye West", "scrobble Loona Heart Attack", "scrobble Mac DeMarco | Chamber of Reflection")]
        [UserSessionRequired]
        [Alias("sb")]
        public async Task ScrobbleAsync([Remainder] string trackValues = null)
        {
            var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id) ?? ConfigData.Data.Bot.Prefix;

            if (string.IsNullOrWhiteSpace(trackValues))
            {
                this._embed.WithColor(DiscordConstants.InformationColorBlue);
                this._embed.WithTitle($"{prfx}scrobble");
                this._embed.WithDescription("Scrobbles a track. You can enter a search value or enter the exact name with separators. " +
                                            "You can only scrobble tracks that already exist on Last.fm.");

                this._embed.AddField("Search for a track to scrobble",
                    $"Format: `{prfx}scrobble SearchValue`\n" +
                    $"`{prfx}sb Stronger Kanye` *(scrobbles Stronger by Kanye West)*\n" +
                    $"`{prfx}scrobble Loona Heart Attack` *(scrobbles Heart Attack (츄) by LOONA)*");

                this._embed.AddField("Or enter the exact name with separators",
                    $"Format: `{prfx}scrobble Artist | Track`\n" +
                    $"`{prfx}scrobble Mac DeMarco | Chamber of Reflection`\n" +
                    $"`{prfx}scrobble Home | Climbing Out`");

                await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
                this.Context.LogCommandUsed(CommandResponse.Help);
                return;
            }

            var track = await this.SearchTrack(trackValues, contextUser.UserNameLastFM, contextUser.SessionKeyLastFm);
            if (track == null)
            {
                return;
            }

            var msg = this.Context.Message as SocketUserMessage;
            if (StackCooldownTarget.Contains(this.Context.Message.Author))
            {
                if (StackCooldownTimer[StackCooldownTarget.IndexOf(msg.Author)].AddSeconds(30) >= DateTimeOffset.Now)
                {
                    var secondsLeft = (int)(StackCooldownTimer[
                            StackCooldownTarget.IndexOf(this.Context.Message.Author as SocketGuildUser)]
                        .AddSeconds(30) - DateTimeOffset.Now).TotalSeconds;
                    if (secondsLeft <= 28)
                    {
                        await ReplyAsync("Please wait before scrobbling to Last.fm again.");
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

            var userTitle = await this._userService.GetUserTitleAsync(this.Context);

            var trackScrobbled = await this._lastFmRepository.ScrobbleAsync(contextUser, track.ArtistName, track.TrackName, track.AlbumName);

            if (trackScrobbled.Success && trackScrobbled.Content.Scrobbles.Attr.Accepted > 0)
            {
                Statistics.LastfmScrobbles.Inc();
                this._embed.WithTitle($"Scrobbled track for {userTitle}");
                this._embed.WithDescription(LastFmRepository.ResponseTrackToLinkedString(track));
            }
            else if (trackScrobbled.Success && trackScrobbled.Content.Scrobbles.Attr.Ignored > 0)
            {
                this._embed.WithTitle($"Last.fm ignored scrobble for {userTitle}");
                var description = new StringBuilder();

                if (!string.IsNullOrWhiteSpace(trackScrobbled.Content.Scrobbles.Scrobble.IgnoredMessage?.Text))
                {
                    description.AppendLine($"Reason: {trackScrobbled.Content.Scrobbles.Scrobble.IgnoredMessage?.Text}");
                }

                description.AppendLine(LastFmRepository.ResponseTrackToLinkedString(track));
                this._embed.WithDescription(description.ToString());
            }
            else
            {
                await this.Context.Message.Channel.SendMessageAsync("Something went wrong while scrobbling track :(.");
                this.Context.LogCommandWithLastFmError(trackScrobbled.Error);
                return;
            }

            await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
            this.Context.LogCommandUsed();
        }

        [Command("toptracks", RunMode = RunMode.Async)]
        [Summary("Shows a list of your or someone else their top tracks over a certain time period.")]
        [Options(Constants.CompactTimePeriodList, Constants.UserMentionExample)]
        [Examples("tt", "toptracks", "tt y 3", "toptracks weekly @user")]
        [Alias("tt", "tl", "tracklist", "tracks", "trackslist", "top tracks", "top track")]
        [UsernameSetRequired]
        [SupportsPagination]
        public async Task TopTracksAsync([Remainder] string extraOptions = null)
        {
            var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

            _ = this.Context.Channel.TriggerTypingAsync();

            var timeSettings = SettingService.GetTimePeriod(extraOptions);
            var userSettings = await this._settingService.GetUser(extraOptions, contextUser, this.Context);
            var amount = SettingService.GetAmount(extraOptions);

            var paginationEnabled = false;
            var pages = new List<PageBuilder>();
            var perms = await this._guildService.CheckSufficientPermissionsAsync(this.Context);
            if (perms.ManageMessages)
            {
                paginationEnabled = true;
            }

            string userTitle;
            if (!userSettings.DifferentUser)
            {
                userTitle = await this._userService.GetUserTitleAsync(this.Context);
            }
            else
            {
                userTitle =
                    $"{userSettings.UserNameLastFm}, requested by {await this._userService.GetUserTitleAsync(this.Context)}";
            }

            var userUrl = $"{Constants.LastFMUserUrl}{userSettings.UserNameLastFm}/library/tracks?{timeSettings.UrlParameter}";
            this._embedAuthor.WithIconUrl(this.Context.User.GetAvatarUrl());
            var trackStrings = amount == 1 ? "track" : "tracks";
            this._embedAuthor.WithName($"Top {timeSettings.Description.ToLower()} {trackStrings} for {userTitle}");
            this._embedAuthor.WithUrl(userUrl);

            var footer = "";
            amount = paginationEnabled ? 100 : amount;

            try
            {
                Response<TopTracksLfmResponse> topTracks;
                if (!timeSettings.UsePlays)
                {
                    topTracks = await this._lastFmRepository.GetTopTracksAsync(userSettings.UserNameLastFm, timeSettings.ApiParameter, amount);

                    if (!topTracks.Success)
                    {
                        this._embed.ErrorResponse(topTracks.Error, topTracks.Message, this.Context);
                        await ReplyAsync("", false, this._embed.Build());
                        return;
                    }

                    if (topTracks.Content?.TopTracks?.Attr != null)
                    {
                        footer = $"{topTracks.Content.TopTracks.Attr.Total} different tracks in this time period";
                    }
                }
                else
                {
                    int userId;
                    if (userSettings.DifferentUser)
                    {
                        var otherUser = await this._userService.GetUserAsync(userSettings.DiscordUserId);
                        if (otherUser.LastIndexed == null)
                        {
                            await this._indexService.IndexUser(otherUser);
                        }
                        else if (contextUser.LastUpdated < DateTime.UtcNow.AddMinutes(-15))
                        {
                            await this._updateService.UpdateUser(otherUser);
                        }

                        userId = otherUser.UserId;
                    }
                    else
                    {
                        if (contextUser.LastIndexed == null)
                        {
                            await this._indexService.IndexUser(contextUser);
                        }
                        else if (contextUser.LastUpdated < DateTime.UtcNow.AddMinutes(-15))
                        {
                            await this._updateService.UpdateUser(contextUser);
                        }

                        userId = contextUser.UserId;
                    }

                    topTracks = await this._playService.GetTopTracks(userId,
                        timeSettings.PlayDays.GetValueOrDefault());

                    footer = $"{topTracks.Content.TopTracks.Track.Count} different tracks in this time period";

                    topTracks.Content.TopTracks.Track = topTracks.Content.TopTracks.Track.Take(amount).ToList();
                }

                if (!topTracks.Content.TopTracks.Track.Any())
                {
                    this._embed.WithDescription("No top tracks returned for selected time period.\n" +
                                                $"View [track history here]({userUrl})");
                    this._embed.WithColor(DiscordConstants.WarningColorOrange);
                    await ReplyAsync("", false, this._embed.Build());
                    this.Context.LogCommandUsed(CommandResponse.NoScrobbles);
                    return;
                }

                var rnd = new Random();
                if (rnd.Next(0, 2) == 1 && topTracks.Content.TopTracks.Track.Count > 10 && !paginationEnabled)
                {
                    footer += $"\nWant pagination? Enable the 'Manage Messages' permission for .fmbot.";
                }

                if (topTracks.Content.TopTracks.Track.Count <= 10)
                {
                    paginationEnabled = false;
                }

                this._embed.WithAuthor(this._embedAuthor);

                var description = "";
                for (var i = 0; i < topTracks.Content.TopTracks.Track.Count; i++)
                {
                    var track = topTracks.Content.TopTracks.Track[i];

                    if (topTracks.Content.TopTracks.Track.Count > 10 && !paginationEnabled)
                    {
                        description += $"{i + 1}. **{track.Artist.Name}** - **{track.Name}** ({track.Playcount} {StringExtensions.GetPlaysString(track.Playcount)}) \n";
                    }
                    else
                    {
                        description += $"{i + 1}. **{track.Artist.Name}** - **[{track.Name}]({track.Url})** ({track.Playcount} {StringExtensions.GetPlaysString(track.Playcount)}) \n";
                    }

                    var pageAmount = i + 1;
                    if (paginationEnabled && (pageAmount > 0 && pageAmount % 10 == 0 || pageAmount == amount))
                    {
                        pages.Add(new PageBuilder().WithDescription(description).WithAuthor(this._embedAuthor).WithFooter(footer));
                        description = "";
                    }
                }

                if (paginationEnabled)
                {
                    var paginator = new StaticPaginatorBuilder()
                        .WithPages(pages)
                        .WithFooter(PaginatorFooter.PageNumber)
                        .WithEmotes(DiscordConstants.PaginationEmotes)
                        .WithTimoutedEmbed(null)
                        .WithCancelledEmbed(null)
                        .WithDeletion(DeletionOptions.Valid)
                        .Build();

                    _ = this.Interactivity.SendPaginatorAsync(paginator, this.Context.Channel, TimeSpan.FromSeconds(DiscordConstants.PaginationTimeoutInSeconds), runOnGateway: false);
                }
                else
                {
                    this._embed.WithAuthor(this._embedAuthor);
                    this._embed.WithDescription(description);
                    this._embed.WithFooter(footer);
                    await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
                }

                this.Context.LogCommandUsed();
            }
            catch (Exception e)
            {
                this.Context.LogCommandException(e);
                await ReplyAsync("Unable to show Last.fm info due to an internal error.");
            }
        }

        [Command("whoknowstrack", RunMode = RunMode.Async)]
        [Summary("Shows what other users listen to a track in your server")]
        [Examples("wt", "whoknowstrack", "whoknowstrack Hothouse Flowers Don't Go", "whoknowstrack Natasha Bedingfield | Unwritten")]
        [Alias("wt", "wkt", "wktr", "wtr", "wktrack", "wk track", "whoknows track")]
        [UsernameSetRequired]
        [GuildOnly]
        public async Task WhoKnowsTrackAsync([Remainder] string trackValues = null)
        {
            var userSettings = await this._userService.GetUserSettingsAsync(this.Context.User);
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id) ?? ConfigData.Data.Bot.Prefix;

            var lastIndex = await this._guildService.GetGuildIndexTimestampAsync(this.Context.Guild);

            if (lastIndex == null)
            {
                await ReplyAsync("This server hasn't been indexed yet.\n" +
                                 $"Please run `{prfx}index` to index this server.\n" +
                                 $"Note that this can take some time on large servers.");
                this.Context.LogCommandUsed(CommandResponse.IndexRequired);
                return;
            }
            if (lastIndex < DateTime.UtcNow.AddDays(-100))
            {
                await ReplyAsync("Server index data is out of date, it was last updated over 100 days ago.\n" +
                                 $"Please run `{prfx}index` to re-index this server.");
                this.Context.LogCommandUsed(CommandResponse.IndexRequired);
                return;
            }

            var guildTask = this._guildService.GetFullGuildAsync(this.Context.Guild.Id);

            _ = this.Context.Channel.TriggerTypingAsync();

            var track = await this.SearchTrack(trackValues, userSettings.UserNameLastFM, userSettings.SessionKeyLastFm);
            if (track == null)
            {
                return;
            }

            await this._spotifyService.GetOrStoreTrackAsync(track);

            var trackName = $"{track.TrackName} by {track.ArtistName}";

            try
            {
                var guild = await guildTask;

                var currentUser = await this._indexService.GetOrAddUserToGuild(guild, await this.Context.Guild.GetUserAsync(userSettings.DiscordUserId), userSettings);

                if (!guild.GuildUsers.Select(s => s.UserId).Contains(userSettings.UserId))
                {
                    guild.GuildUsers.Add(currentUser);
                }

                await this._indexService.UpdateUserName(currentUser, await this.Context.Guild.GetUserAsync(userSettings.DiscordUserId));

                var usersWithTrack = await WhoKnowsTrackService.GetIndexedUsersForTrack(this.Context, guild.GuildId, track.ArtistName, track.TrackName);

                if (track.UserPlaycount.HasValue && track.UserPlaycount != 0)
                {
                    usersWithTrack = WhoKnowsService.AddOrReplaceUserToIndexList(usersWithTrack, currentUser, trackName, track.UserPlaycount.Value);
                }

                var filteredUsersWithTrack = WhoKnowsService.FilterGuildUsersAsync(usersWithTrack, guild);

                var serverUsers = WhoKnowsService.WhoKnowsListToString(filteredUsersWithTrack, userSettings.UserId, PrivacyLevel.Server);
                if (filteredUsersWithTrack.Count == 0)
                {
                    serverUsers = "Nobody in this server (not even you) has listened to this track.";
                }

                this._embed.WithDescription(serverUsers);

                var userTitle = await this._userService.GetUserTitleAsync(this.Context);
                var footer = $"WhoKnows track requested by {userTitle}";

                var rnd = new Random();
                if (rnd.Next(0, 10) == 1 && lastIndex < DateTime.UtcNow.AddDays(-15))
                {
                    footer += $"\nMissing members? Update with {prfx}index";
                }

                if (filteredUsersWithTrack.Any() && filteredUsersWithTrack.Count > 1)
                {
                    var serverListeners = filteredUsersWithTrack.Count;
                    var serverPlaycount = filteredUsersWithTrack.Sum(a => a.Playcount);
                    var avgServerPlaycount = filteredUsersWithTrack.Average(a => a.Playcount);

                    footer += $"\n{serverListeners} {StringExtensions.GetListenersString(serverListeners)} - ";
                    footer += $"{serverPlaycount} total {StringExtensions.GetPlaysString(serverPlaycount)} - ";
                    footer += $"{(int)avgServerPlaycount} avg {StringExtensions.GetPlaysString((int)avgServerPlaycount)}";
                }

                if (usersWithTrack.Count > filteredUsersWithTrack.Count)
                {
                    var filteredAmount = usersWithTrack.Count - filteredUsersWithTrack.Count;
                    footer += $"\n{filteredAmount} inactive/blocked users filtered";
                }

                this._embed.WithTitle($"{trackName} in {this.Context.Guild.Name}");

                if (track.TrackUrl != null)
                {
                    this._embed.WithUrl(track.TrackUrl);
                }

                this._embedFooter.WithText(footer);
                this._embed.WithFooter(this._embedFooter);

                await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());

                this.Context.LogCommandUsed();
            }
            catch (Exception e)
            {
                this.Context.LogCommandException(e);
                await ReplyAsync("Something went wrong while using whoknows track. Please let us know as this feature is in beta.");
            }
        }


        [Command("globalwhoknowstrack", RunMode = RunMode.Async)]
        [Summary("Shows what other users listen to a track in .fmbot")]
        [Examples("gwt", "globalwhoknowstrack", "globalwhoknowstrack Hothouse Flowers Don't Go", "globalwhoknowstrack Natasha Bedingfield | Unwritten")]
        [Alias("gwt", "gwkt", "gwtr", "gwktr", "globalwhoknows track")]
        [UsernameSetRequired]
        [GuildOnly]
        public async Task GlobalWhoKnowsTrackAsync([Remainder] string trackValues = null)
        {
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id) ?? ConfigData.Data.Bot.Prefix;

            var lastIndex = await this._guildService.GetGuildIndexTimestampAsync(this.Context.Guild);

            if (lastIndex == null)
            {
                await ReplyAsync("This server hasn't been indexed yet.\n" +
                                 $"Please run `{prfx}index` to index this server.\n" +
                                 $"Note that this can take some time on large servers.");
                this.Context.LogCommandUsed(CommandResponse.IndexRequired);
                return;
            }
            if (lastIndex < DateTime.UtcNow.AddDays(-100))
            {
                await ReplyAsync("Server index data is out of date, it was last updated over 100 days ago.\n" +
                                 $"Please run `{prfx}index` to re-index this server.");
                this.Context.LogCommandUsed(CommandResponse.IndexRequired);
                return;
            }


            var guildTask = this._guildService.GetFullGuildAsync(this.Context.Guild.Id);
            _ = this.Context.Channel.TriggerTypingAsync();

            var userSettings = await this._userService.GetUserSettingsAsync(this.Context.User);

            var currentSettings = new WhoKnowsSettings
            {
                HidePrivateUsers = false,
                NewSearchValue = trackValues
            };
            var settings = this._settingService.SetWhoKnowsSettings(currentSettings, trackValues);

            var track = await this.SearchTrack(settings.NewSearchValue, userSettings.UserNameLastFM, userSettings.SessionKeyLastFm);
            if (track == null)
            {
                return;
            }

            var trackName = $"{track.TrackName} by {track.ArtistName}";

            try
            {
                var usersWithArtist = await WhoKnowsTrackService.GetGlobalUsersForTrack(this.Context, track.ArtistName, track.TrackName);

                if (track.UserPlaycount != 0 && this.Context.Guild != null)
                {
                    var discordGuildUser = await this.Context.Guild.GetUserAsync(userSettings.DiscordUserId);
                    var guildUser = new GuildUser
                    {
                        UserName = discordGuildUser != null ? discordGuildUser.Nickname ?? discordGuildUser.Username : userSettings.UserNameLastFM,
                        User = userSettings
                    };
                    usersWithArtist = WhoKnowsService.AddOrReplaceUserToIndexList(usersWithArtist, guildUser, trackName, track.UserPlaycount);
                }

                var guild = await guildTask;

                var filteredUsersWithAlbum = await this._whoKnowsService.FilterGlobalUsersAsync(usersWithArtist);

                filteredUsersWithAlbum =
                    WhoKnowsService.ShowGuildMembersInGlobalWhoKnowsAsync(filteredUsersWithAlbum, guild.GuildUsers.ToList());

                var serverUsers = WhoKnowsService.WhoKnowsListToString(filteredUsersWithAlbum, userSettings.UserId, PrivacyLevel.Global, hidePrivateUsers: settings.HidePrivateUsers);
                if (!filteredUsersWithAlbum.Any())
                {
                    serverUsers = "Nobody that uses .fmbot has listened to this track.";
                }

                this._embed.WithDescription(serverUsers);

                var userTitle = await this._userService.GetUserTitleAsync(this.Context);
                var footer = $"Global WhoKnows track requested by {userTitle}";

                if (filteredUsersWithAlbum.Any() && filteredUsersWithAlbum.Count() > 1)
                {
                    var serverListeners = filteredUsersWithAlbum.Count();
                    var serverPlaycount = filteredUsersWithAlbum.Sum(a => a.Playcount);
                    var avgServerPlaycount = filteredUsersWithAlbum.Average(a => a.Playcount);

                    footer += $"\n{serverListeners} {StringExtensions.GetListenersString(serverListeners)} - ";
                    footer += $"{serverPlaycount} total {StringExtensions.GetPlaysString(serverPlaycount)} - ";
                    footer += $"{(int)avgServerPlaycount} avg {StringExtensions.GetPlaysString((int)avgServerPlaycount)}";
                }

                if (userSettings.PrivacyLevel != PrivacyLevel.Global)
                {
                    footer += $"\nYou are currently not globally visible - use '{prfx}privacy global' to enable.";
                }

                if (settings.HidePrivateUsers)
                {
                    footer += "\nAll private users are hidden from results";
                }

                this._embed.WithTitle($"{trackName} globally");

                if (!string.IsNullOrWhiteSpace(track.TrackUrl))
                {
                    this._embed.WithUrl(track.TrackUrl);
                }

                this._embedFooter.WithText(footer);
                this._embed.WithFooter(this._embedFooter);

                await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());

                this.Context.LogCommandUsed();
            }
            catch (Exception e)
            {
                if (!string.IsNullOrEmpty(e.Message) && e.Message.Contains("The server responded with error 50013: Missing Permissions"))
                {
                    this.Context.LogCommandException(e);
                    await ReplyAsync("Error while replying: The bot is missing permissions.\n" +
                                     "Make sure it has permission to 'Embed links' and 'Attach Images'");
                }
                else
                {
                    this.Context.LogCommandException(e);
                    await ReplyAsync("Something went wrong while using global whoknows track.");
                }
            }
        }

        [Command("servertracks", RunMode = RunMode.Async)]
        [Summary("Shows top tracks for your server")]
        [Options("Time periods: `weekly`, `monthly` and `alltime`", "Order options: `plays` and `listeners`")]
        [Examples("st", "st a p", "servertracks", "servertracks alltime", "servertracks listeners weekly")]
        [Alias("st", "stt", "servertoptracks", "servertrack", "server tracks")]
        [GuildOnly]
        public async Task GuildTracksAsync(params string[] extraOptions)
        {
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id) ?? ConfigData.Data.Bot.Prefix;
            var guild = await this._guildService.GetFullGuildAsync(this.Context.Guild.Id);

            if (guild.LastIndexed == null)
            {
                await ReplyAsync("This server hasn't been indexed yet.\n" +
                                 $"Please run `{prfx}index` to index this server.\n" +
                                 $"Note that this can take some time on large servers.");
                this.Context.LogCommandUsed(CommandResponse.IndexRequired);
                return;
            }
            if (guild.LastIndexed < DateTime.UtcNow.AddDays(-100))
            {
                await ReplyAsync("Server index data is out of date, it was last updated over 100 days ago.\n" +
                                 $"Please run `{prfx}index` to re-index this server.");
                this.Context.LogCommandUsed(CommandResponse.IndexRequired);
                return;
            }

            _ = this.Context.Channel.TriggerTypingAsync();

            var serverTrackSettings = new GuildRankingSettings
            {
                ChartTimePeriod = ChartTimePeriod.Weekly,
                OrderType = OrderType.Listeners,
                AmountOfDays = 7
            };

            serverTrackSettings = SettingService.SetGuildRankingSettings(serverTrackSettings, extraOptions);

            var description = "";
            var footer = "";

            if (guild.GuildUsers != null && guild.GuildUsers.Count > 500 && serverTrackSettings.ChartTimePeriod == ChartTimePeriod.Monthly)
            {
                serverTrackSettings.AmountOfDays = 7;
                serverTrackSettings.ChartTimePeriod = ChartTimePeriod.Weekly;
                footer += "Sorry, monthly time period is not supported on large servers.\n";
            }

            try
            {
                IReadOnlyList<ListTrack> topGuildTracks;
                if (serverTrackSettings.ChartTimePeriod == ChartTimePeriod.AllTime)
                {
                    topGuildTracks = await WhoKnowsTrackService.GetTopAllTimeTracksForGuild(guild.GuildId, serverTrackSettings.OrderType);
                    this._embed.WithTitle($"Top alltime tracks in {this.Context.Guild.Name}");
                }
                else if (serverTrackSettings.ChartTimePeriod == ChartTimePeriod.Weekly)
                {
                    topGuildTracks = await WhoKnowsPlayService.GetTopTracksForGuild(guild.GuildId, serverTrackSettings.OrderType, serverTrackSettings.AmountOfDays);
                    this._embed.WithTitle($"Top weekly tracks in {this.Context.Guild.Name}");
                }
                else
                {
                    topGuildTracks = await WhoKnowsPlayService.GetTopTracksForGuild(guild.GuildId, serverTrackSettings.OrderType, serverTrackSettings.AmountOfDays);
                    this._embed.WithTitle($"Top monthly tracks in {this.Context.Guild.Name}");
                }

                if (serverTrackSettings.OrderType == OrderType.Listeners)
                {
                    footer += "Listeners / Plays - Ordered by listeners\n";
                }
                else
                {
                    footer += "Listeners / Plays  - Ordered by plays\n";
                }

                foreach (var track in topGuildTracks)
                {
                    description += $"`{track.ListenerCount}` / `{track.TotalPlaycount}` | **{track.TrackName}** by **{track.ArtistName}**\n";
                }

                this._embed.WithDescription(description);

                var rnd = new Random();
                var randomHintNumber = rnd.Next(0, 5);
                if (randomHintNumber == 1)
                {
                    footer += $"View specific track listeners with {prfx}whoknowstrack\n";
                }
                else if (randomHintNumber == 2)
                {
                    footer += $"Available time periods: alltime and weekly\n";
                }
                else if (randomHintNumber == 3)
                {
                    footer += $"Available sorting options: plays and listeners\n";
                }
                if (guild.LastIndexed < DateTime.UtcNow.AddDays(-7) && randomHintNumber == 4)
                {
                    footer += $"Missing members? Update with {prfx}index\n";
                }

                this._embedFooter.WithText(footer);
                this._embed.WithFooter(this._embedFooter);

                await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
                this.Context.LogCommandUsed();
            }
            catch (Exception e)
            {
                this.Context.LogCommandException(e);
                await ReplyAsync(
                    "Something went wrong while using servertracks. Please report this issue.");
            }
        }

        private async Task<TrackInfo> SearchTrack(string trackValues, string lastFmUserName, string sessionKey = null, string otherUserUsername = null)
        {
            string searchValue;
            if (!string.IsNullOrWhiteSpace(trackValues) && trackValues.Length != 0)
            {
                searchValue = trackValues;

                if (trackValues.Contains(" | "))
                {
                    if (otherUserUsername != null)
                    {
                        lastFmUserName = otherUserUsername;
                    }

                    var trackInfo = await this._lastFmRepository.GetTrackInfoAsync(searchValue.Split(" | ")[1], searchValue.Split(" | ")[0],
                        lastFmUserName);
                    if (!trackInfo.Success && trackInfo.Error == ResponseStatus.MissingParameters)
                    {
                        this._embed.WithDescription($"Track `{searchValue.Split(" | ")[1]}` by `{searchValue.Split(" | ")[0]}` could not be found, please check your search values and try again.");
                        await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
                        this.Context.LogCommandUsed(CommandResponse.NotFound);
                        return null;
                    }
                    if (!trackInfo.Success || trackInfo.Content == null)
                    {
                        this._embed.ErrorResponse(trackInfo.Error, trackInfo.Message, this.Context, "track");
                        await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
                        this.Context.LogCommandUsed(CommandResponse.LastFmError);
                        return null;
                    }
                    return trackInfo.Content;
                }
            }
            else
            {
                var recentScrobbles = await this._lastFmRepository.GetRecentTracksAsync(lastFmUserName, 1, useCache: true, sessionKey: sessionKey);

                if (await ErrorService.RecentScrobbleCallFailedReply(recentScrobbles, lastFmUserName, this.Context))
                {
                    return null;
                }

                if (otherUserUsername != null)
                {
                    lastFmUserName = otherUserUsername;
                }

                var lastPlayedTrack = recentScrobbles.Content.RecentTracks[0];
                var trackInfo = await this._lastFmRepository.GetTrackInfoAsync(lastPlayedTrack.TrackName, lastPlayedTrack.ArtistName,
                    lastFmUserName);

                if (trackInfo == null)
                {
                    this._embed.WithDescription($"Last.fm did not return a result for **{lastPlayedTrack.TrackName}** by **{lastPlayedTrack.ArtistName}**.\n\n" +
                                                $"This usually happens on recently released tracks. Please try again later.");

                    await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());

                    this.Context.LogCommandUsed(CommandResponse.NotFound);
                    return null;
                }

                return trackInfo.Content;
            }

            var result = await this._lastFmRepository.SearchTrackAsync(searchValue);
            if (result.Success && result.Content.Any())
            {
                var track = result.Content[0];

                if (otherUserUsername != null)
                {
                    lastFmUserName = otherUserUsername;
                }

                var trackInfo = await this._lastFmRepository.GetTrackInfoAsync(track.Name, track.ArtistName,
                    lastFmUserName);

                if (trackInfo.Content == null || !trackInfo.Success)
                {
                    this._embed.WithDescription($"Last.fm did not return a result for **{track.Name}** by **{track.ArtistName}**.\n" +
                                                $"This usually happens on recently released tracks. Please try again later.");

                    await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
                    this.Context.LogCommandUsed(CommandResponse.NotFound);
                    return null;
                }

                return trackInfo.Content;
            }

            if (result.Success)
            {
                this._embed.WithDescription($"Track could not be found, please check your search values and try again.");
                await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
                this.Context.LogCommandUsed(CommandResponse.NotFound);
                return null;
            }

            this._embed.WithDescription($"Last.fm returned an error: {result.Status}");
            await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
            this.Context.LogCommandUsed(CommandResponse.LastFmError);
            return null;
        }
    }
}
