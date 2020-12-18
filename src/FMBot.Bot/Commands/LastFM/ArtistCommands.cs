using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Discord;
using Discord.API.Rest;
using Discord.Commands;
using FMBot.Bot.Attributes;
using FMBot.Bot.Configurations;
using FMBot.Bot.Extensions;
using FMBot.Bot.Interfaces;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Bot.Services.WhoKnows;
using FMBot.Domain;
using FMBot.Domain.Models;
using FMBot.LastFM.Domain.Models;
using FMBot.LastFM.Domain.Types;
using FMBot.LastFM.Services;
using FMBot.Persistence.Domain.Models;
using Interactivity;
using Interactivity.Pagination;

namespace FMBot.Bot.Commands.LastFM
{
    [Name("Artists")]
    public class ArtistCommands : ModuleBase
    {
        private readonly ArtistsService _artistsService;
        private readonly CrownService _crownService;
        private readonly GuildService _guildService;
        private readonly IIndexService _indexService;
        private readonly ILastfmApi _lastFmApi;
        private InteractivityService Interactivity { get; set; }
        private readonly IPrefixService _prefixService;
        private readonly IUpdateService _updateService;
        private readonly LastFmService _lastFmService;
        private readonly PlayService _playService;
        private readonly SettingService _settingService;
        private readonly SpotifyService _spotifyService;
        private readonly UserService _userService;
        private readonly WhoKnowsArtistService _whoKnowArtistService;
        private readonly WhoKnowsPlayService _whoKnowsPlayService;

        private readonly EmbedAuthorBuilder _embedAuthor;
        private readonly EmbedBuilder _embed;
        private readonly EmbedFooterBuilder _embedFooter;

        public ArtistCommands(
                ArtistsService artistsService,
                CrownService crownService,
                GuildService guildService,
                IIndexService indexService,
                ILastfmApi lastFmApi,
                IPrefixService prefixService,
                IUpdateService updateService,
                LastFmService lastFmService,
                PlayService playService,
                SettingService settingService,
                SpotifyService spotifyService,
                UserService userService,
                WhoKnowsArtistService whoKnowsArtistService,
                WhoKnowsPlayService whoKnowsPlayService,
                InteractivityService interactivity)
        {
            this._artistsService = artistsService;
            this._crownService = crownService;
            this._guildService = guildService;
            this._indexService = indexService;
            this._lastFmApi = lastFmApi;
            this._lastFmService = lastFmService;
            this._playService = playService;
            this._prefixService = prefixService;
            this._settingService = settingService;
            this._spotifyService = spotifyService;
            this._updateService = updateService;
            this._userService = userService;
            this._whoKnowArtistService = whoKnowsArtistService;
            this._whoKnowsPlayService = whoKnowsPlayService;
            this.Interactivity = interactivity;

            this._embedAuthor = new EmbedAuthorBuilder();
            this._embed = new EmbedBuilder()
                .WithColor(DiscordConstants.LastFmColorRed);
            this._embedFooter = new EmbedFooterBuilder();
        }

        [Command("artist", RunMode = RunMode.Async)]
        [Summary("Displays artist info and stats.")]
        [Alias("a")]
        [UsernameSetRequired]
        public async Task ArtistAsync([Remainder] string artistValues = null)
        {
            var userSettings = await this._userService.GetUserSettingsAsync(this.Context.User);
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id) ?? ConfigData.Data.Bot.Prefix;

            var artist = await GetArtistOrHelp(artistValues, userSettings, "artist", prfx);
            if (artist == null)
            {
                this.Context.LogCommandUsed(CommandResponse.NotFound);
                return;
            }

            _ = this.Context.Channel.TriggerTypingAsync();

            var queryParams = new Dictionary<string, string>
            {
                {"artist", artist },
                {"username", userSettings.UserNameLastFM },
                {"autocorrect", "1"}
            };

            var artistCallTask = this._lastFmApi.CallApiAsync<ArtistResponse>(queryParams, Call.ArtistInfo);

            if (userSettings.LastUpdated < DateTime.UtcNow.AddHours(-1))
            {
                if (userSettings.LastIndexed == null)
                {
                    await this._indexService.IndexUser(userSettings);
                }
                else
                {
                    await this._updateService.UpdateUser(userSettings);
                }
            }

            var artistCall = await artistCallTask;

            if (!artistCall.Success)
            {
                this._embed.ErrorResponse(artistCall.Error, artistCall.Message, this.Context);
                await ReplyAsync("", false, this._embed.Build());
                this.Context.LogCommandWithLastFmError(artistCall.Error);
                return;
            }

            var spotifyImageSearchTask = this._spotifyService.GetOrStoreArtistImageAsync(artistCall.Content, artist);

            var artistInfo = artistCall.Content.Artist;
            var spotifyImage = await spotifyImageSearchTask;

            if (spotifyImage != null)
            {
                this._embed.WithThumbnailUrl(spotifyImage);
                this._embedFooter.WithText("Image source: Spotify");
                this._embed.WithFooter(this._embedFooter);
            }

            var userTitle = await this._userService.GetUserTitleAsync(this.Context);

            this._embedAuthor.WithIconUrl(this.Context.User.GetAvatarUrl());
            this._embedAuthor.WithName($"Artist info about {artistInfo.Name} for {userTitle}");
            this._embedAuthor.WithUrl(artistInfo.Url);
            this._embed.WithAuthor(this._embedAuthor);

            if (!this._guildService.CheckIfDM(this.Context))
            {
                var serverStats = "";
                var guild = await this._guildService.GetGuildAsync(this.Context.Guild.Id);
                var filteredGuildUsers = this._guildService.FilterGuildUsersAsync(guild);

                if (guild.LastIndexed != null)
                {
                    var serverListenersTask = this._whoKnowArtistService.GetArtistListenerCountForServer(filteredGuildUsers, artistInfo.Name);
                    var serverPlaycountTask = this._whoKnowArtistService.GetArtistPlayCountForServer(filteredGuildUsers, artistInfo.Name);
                    var avgServerListenerPlaycountTask = this._whoKnowArtistService.GetArtistAverageListenerPlaycountForServer(filteredGuildUsers, artistInfo.Name);
                    var serverPlaycountLastWeekTask = this._whoKnowArtistService.GetWeekArtistPlaycountForGuildAsync(filteredGuildUsers, artistInfo.Name);

                    var serverListeners = await serverListenersTask;
                    var serverPlaycount = await serverPlaycountTask;
                    var avgServerListenerPlaycount = await avgServerListenerPlaycountTask;
                    var serverPlaycountLastWeek = await serverPlaycountLastWeekTask;

                    serverStats += $"`{serverListeners}` {StringExtensions.GetListenersString(serverListeners)}";
                    serverStats += $"\n`{serverPlaycount}` total {StringExtensions.GetPlaysString(serverPlaycount)}";
                    serverStats += $"\n`{(int)avgServerListenerPlaycount}` avg {StringExtensions.GetPlaysString((int)avgServerListenerPlaycount)}";
                    serverStats += $"\n`{serverPlaycountLastWeek}` {StringExtensions.GetPlaysString(serverPlaycountLastWeek)} last week";

                    if (guild.GuildUsers.Count > filteredGuildUsers.Count)
                    {
                        var filteredAmount = guild.GuildUsers.Count - filteredGuildUsers.Count;
                        serverStats += $"\n`{filteredAmount}` users filtered";
                    }
                }
                else
                {
                    serverStats += "Run `.fmindex` to get server stats";
                }

                this._embed.AddField("Server stats", serverStats, true);
            }

            var globalStats = "";
            globalStats += $"`{artistInfo.Stats.Listeners}` {StringExtensions.GetListenersString(artistInfo.Stats.Listeners)}";
            globalStats += $"\n`{artistInfo.Stats.Playcount}` global {StringExtensions.GetPlaysString(artistInfo.Stats.Playcount)}";
            if (artistInfo.Stats.Userplaycount.HasValue)
            {
                globalStats += $"\n`{artistInfo.Stats.Userplaycount}` {StringExtensions.GetPlaysString(artistInfo.Stats.Userplaycount)} by you";
                globalStats += $"\n`{await this._playService.GetWeekArtistPlaycountAsync(userSettings.UserId, artistInfo.Name)}` by you last week";
            }

            this._embed.AddField("Last.fm stats", globalStats, true);

            if (!string.IsNullOrWhiteSpace(artistInfo.Bio.Content))
            {
                var linktext = $"<a href=\"{artistInfo.Url}\">Read more on Last.fm</a>";
                var filteredSummary = artistInfo.Bio.Summary.Replace(linktext, "");
                if (!string.IsNullOrWhiteSpace(filteredSummary))
                {
                    this._embed.AddField("Summary", filteredSummary);
                }
            }

            if (artistInfo.Tags.Tag.Any())
            {
                var tags = LastFmService.TagsToLinkedString(artistInfo.Tags);

                this._embed.AddField("Tags", tags);
            }

            await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
            this.Context.LogCommandUsed();
        }

        [Command("artisttracks", RunMode = RunMode.Async)]
        [Summary("Displays top tracks for an artist.")]
        [Alias("at", "att", "artisttrack", "artist track", "artist tracks", "artistrack", "artisttoptracks", "artisttoptrack")]
        [UsernameSetRequired]
        public async Task ArtistTracksAsync([Remainder] string artistValues = null)
        {
            var user = await this._userService.GetUserSettingsAsync(this.Context.User);
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id) ?? ConfigData.Data.Bot.Prefix;

            var artist = await GetArtistOrHelp(artistValues, user, "artisttracks", prfx);
            if (artist == null)
            {
                this.Context.LogCommandUsed(CommandResponse.NotFound);
                return;
            }

            _ = this.Context.Channel.TriggerTypingAsync();

            if (user.LastIndexed == null)
            {
                await this._indexService.IndexUser(user);
            }
            else if (user.LastUpdated < DateTime.UtcNow.AddMinutes(-20))
            {
                await this._updateService.UpdateUser(user);
            }

            var queryParams = new Dictionary<string, string>
            {
                {"artist", artist },
                {"username", user.UserNameLastFM },
                {"autocorrect", "1"}
            };
            var artistCall = await this._lastFmApi.CallApiAsync<ArtistResponse>(queryParams, Call.ArtistInfo);
            if (!artistCall.Success)
            {
                this._embed.ErrorResponse(artistCall.Error, artistCall.Message, this.Context);
                await ReplyAsync("", false, this._embed.Build());
                this.Context.LogCommandWithLastFmError(artistCall.Error);
                return;
            }

            var artistInfo = artistCall.Content.Artist;

            var timeSettings = SettingService.GetTimePeriod(artistValues, ChartTimePeriod.AllTime);

            var timeDescription = timeSettings.Description;
            List<UserTrack> topTracks;
            switch (timeSettings.ChartTimePeriod)
            {
                //case ChartTimePeriod.Weekly:
                //    topTracks = await this._playService.GetTopTracksForArtist(user.UserId, 7, artistInfo.Name);
                //    break;
                //case ChartTimePeriod.Monthly:
                //    topTracks = await this._playService.GetTopTracksForArtist(user.UserId, 31, artistInfo.Name);
                //    break;
                default:
                    timeDescription = "alltime";
                    topTracks = await this._artistsService.GetTopTracksForArtist(user.UserId, artistInfo.Name);
                    break;
            }

            var userTitle = await this._userService.GetUserTitleAsync(this.Context);

            if (topTracks.Count == 0)
            {
                this._embed.WithDescription(
                    $"{userTitle} has no scrobbles for this artist.");
            }
            else
            {

                var description = new StringBuilder();
                for (var i = 0; i < topTracks.Count; i++)
                {
                    var track = topTracks[i];

                    description.AppendLine($"{i + 1}. **{track.Name}** ({track.Playcount} plays)");
                }

                this._embed.WithDescription(description.ToString());

                this._embed.WithFooter($"{userTitle} has {artistInfo.Stats.Userplaycount} total scrobbles on this artist");
            }

            this._embed.WithTitle($"Your top tracks for '{artistInfo.Name}'");

            var url = $"{Constants.LastFMUserUrl}{user.UserNameLastFM}/library/music/{UrlEncoder.Default.Encode(artistInfo.Name)}";
            if (Uri.IsWellFormedUriString(url, UriKind.Absolute))
            {
                this._embed.WithUrl(url);
            }

            await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
            this.Context.LogCommandUsed();
        }

        [Command("artistalbums", RunMode = RunMode.Async)]
        [Summary("Displays top albums for an artist.")]
        [Alias("aa", "aab", "atab", "artistalbum", "artist album", "artist albums", "artistopalbum", "artisttopalbums", "artisttab")]
        [UsernameSetRequired]
        public async Task ArtistAlbumsAsync([Remainder] string artistValues = null)
        {
            var user = await this._userService.GetUserSettingsAsync(this.Context.User);
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id) ?? ConfigData.Data.Bot.Prefix;

            var artist = await GetArtistOrHelp(artistValues, user, "artistalbums", prfx);
            if (artist == null)
            {
                this.Context.LogCommandUsed(CommandResponse.NotFound);
                return;
            }

            _ = this.Context.Channel.TriggerTypingAsync();

            if (user.LastIndexed == null)
            {
                await this._indexService.IndexUser(user);
            }
            else if (user.LastUpdated < DateTime.UtcNow.AddMinutes(-20))
            {
                await this._updateService.UpdateUser(user);
            }

            var queryParams = new Dictionary<string, string>
            {
                {"artist", artist },
                {"username", user.UserNameLastFM },
                {"autocorrect", "1"}
            };
            var artistCall = await this._lastFmApi.CallApiAsync<ArtistResponse>(queryParams, Call.ArtistInfo);
            if (!artistCall.Success)
            {
                this._embed.ErrorResponse(artistCall.Error, artistCall.Message, this.Context);
                await ReplyAsync("", false, this._embed.Build());
                this.Context.LogCommandWithLastFmError(artistCall.Error);
                return;
            }

            var artistInfo = artistCall.Content.Artist;

            var timeSettings = SettingService.GetTimePeriod(artistValues, ChartTimePeriod.AllTime);

            var timeDescription = timeSettings.Description;
            List<UserAlbum> topAlbums;
            switch (timeSettings.ChartTimePeriod)
            {
                //case ChartTimePeriod.Weekly:
                //    topTracks = await this._playService.GetTopTracksForArtist(user.UserId, 7, artistInfo.Name);
                //    break;
                //case ChartTimePeriod.Monthly:
                //    topTracks = await this._playService.GetTopTracksForArtist(user.UserId, 31, artistInfo.Name);
                //    break;
                default:
                    timeDescription = "alltime";
                    topAlbums = await this._artistsService.GetTopAlbumsForArtist(user.UserId, artistInfo.Name);
                    break;
            }

            var userTitle = await this._userService.GetUserTitleAsync(this.Context);
            if (topAlbums.Count == 0)
            {
                this._embed.WithDescription(
                    $"{userTitle} has no scrobbles for this artist or their scrobbles have no album associated with them.");
            }
            else
            {
                var description = new StringBuilder();
                for (var i = 0; i < topAlbums.Count; i++)
                {
                    var album = topAlbums[i];

                    description.AppendLine($"{i + 1}. **{album.Name}** ({album.Playcount} plays)");
                }

                this._embed.WithDescription(description.ToString());

                this._embed.WithFooter($"{userTitle} has {artistInfo.Stats.Userplaycount} total scrobbles on this artist");
            }

            this._embed.WithTitle($"Your top albums for '{artistInfo.Name}'");

            var url = $"{Constants.LastFMUserUrl}{user.UserNameLastFM}/library/music/{UrlEncoder.Default.Encode(artistInfo.Name)}";
            if (Uri.IsWellFormedUriString(url, UriKind.Absolute))
            {
                this._embed.WithUrl(url);
            }

            await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
            this.Context.LogCommandUsed();
        }

        [Command("artistplays", RunMode = RunMode.Async)]
        [Summary("Displays artist playcount.")]
        [Alias("ap", "artist plays")]
        [UsernameSetRequired]
        public async Task ArtistPlaysAsync([Remainder] string artistValues = null)
        {
            var userSettings = await this._userService.GetUserSettingsAsync(this.Context.User);
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id) ?? ConfigData.Data.Bot.Prefix;

            var artist = await GetArtistOrHelp(artistValues, userSettings, "artistplays", prfx);
            if (artist == null)
            {
                this.Context.LogCommandUsed(CommandResponse.NotFound);
                return;
            }

            _ = this.Context.Channel.TriggerTypingAsync();

            var queryParams = new Dictionary<string, string>
            {
                {"artist", artist },
                {"username", userSettings.UserNameLastFM },
                {"autocorrect", "1"}
            };
            var artistCall = await this._lastFmApi.CallApiAsync<ArtistResponse>(queryParams, Call.ArtistInfo);
            if (!artistCall.Success)
            {
                this._embed.ErrorResponse(artistCall.Error, artistCall.Message, this.Context);
                await ReplyAsync("", false, this._embed.Build());
                this.Context.LogCommandWithLastFmError(artistCall.Error);
                return;
            }

            var artistInfo = artistCall.Content.Artist;

            var userTitle = await this._userService.GetUserTitleAsync(this.Context);

            //this._embedAuthor.WithIconUrl(this.Context.User.GetAvatarUrl());
            var desc =
                $"{userTitle} has {artistInfo.Stats.Userplaycount} {StringExtensions.GetPlaysString(artistInfo.Stats.Userplaycount)} for **{artistInfo.Name}**";

            if (userSettings.LastUpdated != null)
            {
                var playsLastWeek =
                    await this._playService.GetWeekArtistPlaycountAsync(userSettings.UserId, artistInfo.Name);
                desc += $" - {playsLastWeek} last week";
            }
            this._embed.WithDescription(desc);
            //this._embedAuthor.WithUrl(artistInfo.Url);
            this._embed.WithAuthor(this._embedAuthor);

            await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
            this.Context.LogCommandUsed();
        }

        [Command("topartists", RunMode = RunMode.Async)]
        [Summary("Displays top artists.")]
        [Alias("al", "as", "ta", "artistlist", "artists", "top artists", "artistslist")]
        [UsernameSetRequired]
        public async Task TopArtistsAsync([Remainder] string extraOptions = null)
        {
            var user = await this._userService.GetUserSettingsAsync(this.Context.User);
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id) ?? ConfigData.Data.Bot.Prefix;

            if (!string.IsNullOrWhiteSpace(extraOptions) && extraOptions.ToLower() == "help")
            {
                this._embed.WithTitle($"{prfx}topartists options");
                this._embed.WithDescription($"- `{Constants.CompactTimePeriodList}`\n" +
                                            $"- `number of artists (max 16)`\n" +
                                            $"- `user mention/id`");

                this._embed.AddField("Example",
                    $"`{prfx}topartists @drasil alltime 11`");

                await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
                this.Context.LogCommandUsed(CommandResponse.Help);
                return;
            }

            _ = this.Context.Channel.TriggerTypingAsync();

            var timePeriodString = extraOptions;
            if (this.Context.InteractionData != null)
            {
                var time = this.Context.InteractionData.Choices.FirstOrDefault(w => w.Name == "time");
                timePeriodString = time?.Value?.ToLower();
            }

            var amountString = extraOptions;
            if (this.Context.InteractionData != null)
            {
                var time = this.Context.InteractionData.Choices.FirstOrDefault(w => w.Name == "amount");
                amountString = time?.Value?.ToLower();
            }

            var timeSettings = SettingService.GetTimePeriod(timePeriodString);
            var userSettings = await this._settingService.GetUser(extraOptions, user, this.Context);
            var amount = SettingService.GetAmount(amountString);

            try
            {
                var description = "";
                if (!timeSettings.UsePlays)
                {
                    var artists = await this._lastFmService.GetTopArtistsAsync(userSettings.UserNameLastFm,
                        timeSettings.LastStatsTimeSpan, amount);

                    if (artists == null || !artists.Any() || !artists.Content.Any())
                    {
                        this._embed.NoScrobblesFoundErrorResponse(artists?.Status, prfx, userSettings.UserNameLastFm);
                        this.Context.LogCommandUsed(CommandResponse.NoScrobbles);
                        await ReplyAsync("", false, this._embed.Build());
                        return;
                    }

                    for (var i = 0; i < artists.Count(); i++)
                    {
                        var artist = artists.Content[i];

                        if (artists.Count() > 10)
                        {
                            description += $"{i + 1}. **{artist.Name}** ({artist.PlayCount} plays) \n";
                        }
                        else
                        {
                            description += $"{i + 1}. **[{artist.Name}]({artist.Url})** ({artist.PlayCount} plays) \n";
                        }

                    }

                    this._embedFooter.WithText($"{artists.TotalItems} different artists in this time period");
                }
                else
                {
                    int userId;
                    if (userSettings.DifferentUser && userSettings.DiscordUserId.HasValue)
                    {
                        var otherUser = await this._userService.GetUserAsync(userSettings.DiscordUserId.Value);
                        if (otherUser.LastIndexed == null)
                        {
                            await this._indexService.IndexUser(otherUser);
                        }
                        else if (user.LastUpdated < DateTime.UtcNow.AddMinutes(-15))
                        {
                            await this._updateService.UpdateUser(otherUser);
                        }

                        userId = otherUser.UserId;
                    }
                    else
                    {
                        if (user.LastIndexed == null)
                        {
                            await this._indexService.IndexUser(user);
                        }
                        else if (user.LastUpdated < DateTime.UtcNow.AddMinutes(-15))
                        {
                            await this._updateService.UpdateUser(user);
                        }

                        userId = user.UserId;
                    }

                    var artists = await this._playService.GetTopArtists(userId,
                        timeSettings.PlayDays.GetValueOrDefault());

                    var amountAvailable = artists.Count < amount ? artists.Count : amount;
                    for (var i = 0; i < amountAvailable; i++)
                    {
                        var artist = artists[i];

                        description += $"{i + 1}. **{artist.Name}** ({artist.Playcount} {StringExtensions.GetPlaysString(artist.Playcount)}) \n";
                    }

                    this._embedFooter.WithText($"{artists.Count} different artists in this time period");
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

                this._embedAuthor.WithIconUrl(this.Context.User.GetAvatarUrl());
                var artistsString = amount == 1 ? "artist" : "artists";
                this._embedAuthor.WithName($"Top {amount} {timeSettings.Description.ToLower()} {artistsString} for {userTitle}");
                this._embedAuthor.WithUrl($"{Constants.LastFMUserUrl}{userSettings.UserNameLastFm}/library/artists?{timeSettings.UrlParameter}");
                this._embed.WithAuthor(this._embedAuthor);

                this._embed.WithDescription(description);
                this._embed.WithFooter(this._embedFooter);

                if (this.Context.InteractionData != null)
                {
                    await this.Context.Channel.SendInteractionMessageAsync(this.Context.InteractionData, embed: this._embed.Build(), type: InteractionMessageType.ChannelMessageWithSource);
                }
                else
                {
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

        //[Command("lazypaginator")]
        public Task LazyPaginatorAsync()
        {
            var paginator = new LazyPaginatorBuilder()
                .WithPageFactory(PageFactory)
                .WithMaxPageIndex(100)
                .WithFooter(PaginatorFooter.PageNumber | PaginatorFooter.Users)
                .WithDefaultEmotes()
                .Build();

            return Interactivity.SendPaginatorAsync(paginator, Context.Channel, TimeSpan.FromMinutes(2));

            Task<PageBuilder> PageFactory(int page)
            {
                return Task.FromResult(new PageBuilder()
                    .WithText((page + 1).ToString())
                    .WithTitle($"Title for page {page + 1}")
                    .WithColor(System.Drawing.Color.FromArgb(page * 1500)));
            }
        }

        [Command("taste", RunMode = RunMode.Async)]
        [Summary("Compare taste to other user.")]
        [UsernameSetRequired]
        [Alias("t")]
        public async Task TasteAsync(string user = null, [Remainder] string extraOptions = null)
        {
            var userSettings = await this._userService.GetUserSettingsAsync(this.Context.User);
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id) ?? ConfigData.Data.Bot.Prefix;

            if (user == "help")
            {
                await ReplyAsync(
                    $"Usage: `{prfx}taste 'last.fm username/ discord mention' '{Constants.CompactTimePeriodList}' 'table/embed'`");
                this.Context.LogCommandUsed(CommandResponse.Help);
                return;
            }

            _ = this.Context.Channel.TriggerTypingAsync();

            var timeType = SettingService.GetTimePeriod(
                extraOptions,
                ChartTimePeriod.AllTime);

            var tasteSettings = new TasteSettings
            {
                ChartTimePeriod = timeType.ChartTimePeriod
            };

            tasteSettings = this._artistsService.SetTasteSettings(tasteSettings, extraOptions);

            try
            {
                var ownLastFmUsername = userSettings.UserNameLastFM;
                string lastfmToCompare = null;

                if (user != null)
                {
                    string alternativeLastFmUserName;

                    if (await this._lastFmService.LastFmUserExistsAsync(user))
                    {
                        alternativeLastFmUserName = user;
                    }
                    else
                    {
                        var otherUser = await this._settingService.GetUserFromString(user);

                        alternativeLastFmUserName = otherUser?.UserNameLastFM;
                    }

                    if (!string.IsNullOrEmpty(alternativeLastFmUserName))
                    {
                        lastfmToCompare = alternativeLastFmUserName;
                    }
                }

                if (lastfmToCompare == null)
                {
                    await ReplyAsync(
                        $"Please enter a valid user to compare your top artists to. \n" +
                        $"Example: `{prfx}taste lastfmusername` or `{prfx}taste @user`");
                    this.Context.LogCommandUsed(CommandResponse.WrongInput);
                    return;
                }
                if (lastfmToCompare.ToLower() == userSettings.UserNameLastFM.ToLower())
                {
                    await ReplyAsync(
                        $"You can't compare your own taste with yourself. For viewing your top artists, use `{prfx}topartists`\n" +
                        $"Please enter a different last.fm username or mention another user.");
                    this.Context.LogCommandUsed(CommandResponse.WrongInput);
                    return;
                }

                tasteSettings.OtherUserLastFmUsername = lastfmToCompare;

                var ownArtistsTask = this._lastFmService.GetTopArtistsAsync(ownLastFmUsername, timeType.LastStatsTimeSpan, 1000);
                var otherArtistsTask = this._lastFmService.GetTopArtistsAsync(lastfmToCompare, timeType.LastStatsTimeSpan, 1000);

                var ownArtists = await ownArtistsTask;
                var otherArtists = await otherArtistsTask;

                if (ownArtists?.Any() != true || otherArtists?.Any() != true)
                {
                    await ReplyAsync(
                        $"You or the other user don't have any artist plays in the selected time period.");
                    this.Context.LogCommandUsed(CommandResponse.NoScrobbles);
                    return;
                }

                this._embedAuthor.WithIconUrl(this.Context.User.GetAvatarUrl());
                this._embedAuthor.WithName($"Top artist comparison - {ownLastFmUsername} vs {lastfmToCompare}");
                this._embedAuthor.WithUrl($"{Constants.LastFMUserUrl}{lastfmToCompare}/library/artists?{timeType.UrlParameter}");
                this._embed.WithAuthor(this._embedAuthor);

                int amount = 14;
                if (tasteSettings.TasteType == TasteType.FullEmbed)
                {
                    var taste = this._artistsService.GetEmbedTaste(ownArtists, otherArtists, amount, timeType.ChartTimePeriod);

                    this._embed.WithDescription(taste.Description);
                    this._embed.AddField("Artist", taste.LeftDescription, true);
                    this._embed.AddField("Plays", taste.RightDescription, true);
                }
                else
                {
                    var taste = this._artistsService.GetTableTaste(ownArtists, otherArtists, amount, timeType.ChartTimePeriod, ownLastFmUsername, lastfmToCompare);

                    this._embed.WithDescription(taste);
                }

                await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
                this.Context.LogCommandUsed();
            }
            catch (Exception e)
            {
                this.Context.LogCommandException(e);
                await ReplyAsync("Unable to show Last.fm info due to an internal error.");
            }
        }

        [Command("whoknows", RunMode = RunMode.Async)]
        [Summary("Shows what other users listen to the same artist in your server")]
        [Alias("w", "wk", "whoknows artist")]
        [UsernameSetRequired]
        [GuildOnly]
        public async Task WhoKnowsAsync([Remainder] string artistValues = null)
        {
            var userSettings = await this._userService.GetUserSettingsAsync(this.Context.User);
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id) ?? ConfigData.Data.Bot.Prefix;

            var lastIndex = await this._guildService.GetGuildIndexTimestampAsync(this.Context.Guild);

            if (lastIndex == null)
            {
                await ReplyAsync("This server hasn't been indexed yet.\n" +
                                 $"Please run `{prfx}index` to index this server.");
                this.Context.LogCommandUsed(CommandResponse.IndexRequired);
                return;
            }
            if (lastIndex < DateTime.UtcNow.AddDays(-50))
            {
                await ReplyAsync("Server index data is out of date, it was last updated over 50 days ago.\n" +
                                 $"Please run `{prfx}index` to re-index this server.");
                this.Context.LogCommandUsed(CommandResponse.IndexRequired);
                return;
            }

            try
            {
                var guildTask = this._guildService.GetGuildAsync(this.Context.Guild.Id);

                _ = this.Context.Channel.TriggerTypingAsync();

                var artistQuery = await GetArtistOrHelp(artistValues, userSettings, "whoknows", prfx);
                if (artistQuery == null)
                {
                    this.Context.LogCommandUsed(CommandResponse.NotFound);
                    return;
                }

                string artistName;
                string artistUrl;
                string spotifyImageUrl;
                long? userPlaycount;

                var cachedArtist = await this._artistsService.GetArtistFromDatabase(artistQuery);

                if (userSettings.LastIndexed != null && userSettings.LastUpdated < DateTime.UtcNow.AddHours(-1))
                {
                    await this._updateService.UpdateUser(userSettings);
                    userSettings = await this._userService.GetUserSettingsAsync(this.Context.User);
                }
                if (userSettings.LastUpdated > DateTime.UtcNow.AddHours(-1) && cachedArtist != null)
                {
                    artistName = cachedArtist.Name;
                    artistUrl = cachedArtist.LastFmUrl;
                    spotifyImageUrl = cachedArtist.SpotifyImageUrl;

                    userPlaycount = await this._whoKnowArtistService.GetArtistPlayCountForUser(artistName, userSettings.UserId);
                }
                else
                {
                    var queryParams = new Dictionary<string, string>
                    {
                        {"artist", artistQuery },
                        {"username", userSettings.UserNameLastFM },
                        {"autocorrect", "1"}
                    };

                    var artistCall = await this._lastFmApi.CallApiAsync<ArtistResponse>(queryParams, Call.ArtistInfo);

                    if (!artistCall.Success)
                    {
                        this._embed.ErrorResponse(artistCall.Error, artistCall.Message, this.Context);
                        await ReplyAsync("", false, this._embed.Build());
                        this.Context.LogCommandWithLastFmError(artistCall.Error);
                        return;
                    }

                    artistName = artistCall.Content.Artist.Name;
                    artistUrl = artistCall.Content.Artist.Url;

                    var spotifyArtistResults = await this._spotifyService.GetOrStoreArtistImageAsync(artistCall.Content, artistQuery);
                    spotifyImageUrl = spotifyArtistResults;
                    userPlaycount = artistCall.Content.Artist.Stats.Userplaycount;
                }

                var guild = await guildTask;

                var filteredGuildUsers = this._guildService.FilterGuildUsersAsync(guild);

                var currentUser = await this._indexService.GetOrAddUserToGuild(guild, await this.Context.Guild.GetUserAsync(userSettings.DiscordUserId), userSettings);

                if (!guild.GuildUsers.Select(s => s.UserId).Contains(userSettings.UserId))
                {
                    guild.GuildUsers.Add(currentUser);
                }

                await this._indexService.UpdateUserName(currentUser, await this.Context.Guild.GetUserAsync(userSettings.DiscordUserId));

                var usersWithArtist = await this._whoKnowArtistService.GetIndexedUsersForArtist(this.Context, filteredGuildUsers, artistName);

                Statistics.LastfmApiCalls.Inc();

                if (userPlaycount != 0)
                {
                    usersWithArtist = WhoKnowsService.AddOrReplaceUserToIndexList(usersWithArtist, currentUser, artistName, userPlaycount);
                }

                CrownModel crownModel = null;
                if (guild.CrownsDisabled != true && usersWithArtist.Count >= 1)
                {
                    crownModel =
                        await this._crownService.GetAndUpdateCrownForArtist(usersWithArtist, guild, artistName);
                }

                var serverUsers = WhoKnowsService.WhoKnowsListToString(usersWithArtist, crownModel);
                if (usersWithArtist.Count == 0)
                {
                    serverUsers = "Nobody in this server (not even you) has listened to this artist.";
                }

                this._embed.WithDescription(serverUsers);

                var userTitle = await this._userService.GetUserTitleAsync(this.Context);
                var footer = $"WhoKnows artist requested by {userTitle}";

                var rnd = new Random();
                if (rnd.Next(0, 10) == 1 && lastIndex < DateTime.UtcNow.AddDays(-15))
                {
                    footer += $"\nMissing members? Update with {prfx}index";
                }

                if (filteredGuildUsers.Count < 500)
                {
                    var serverListenersTask = this._whoKnowArtistService.GetArtistListenerCountForServer(filteredGuildUsers, artistName);
                    var serverPlaycountTask = this._whoKnowArtistService.GetArtistPlayCountForServer(filteredGuildUsers, artistName);
                    var avgServerListenerPlaycountTask = this._whoKnowArtistService.GetArtistAverageListenerPlaycountForServer(filteredGuildUsers, artistName);

                    var serverListeners = await serverListenersTask;
                    var serverPlaycount = await serverPlaycountTask;
                    var avgServerListenerPlaycount = await avgServerListenerPlaycountTask;

                    footer += $"\n{serverListeners} {StringExtensions.GetListenersString(serverListeners)} - ";
                    footer += $"{serverPlaycount} total {StringExtensions.GetPlaysString(serverPlaycount)} - ";
                    footer += $"{(int)avgServerListenerPlaycount} avg {StringExtensions.GetPlaysString((int)avgServerListenerPlaycount)}";
                }
                else if (filteredGuildUsers.Count < 550)
                {
                    footer += $"\nView server artist averages in `{prfx}artist`";
                }

                var guildAlsoPlaying = await this._whoKnowsPlayService.GuildAlsoPlayingArtist(userSettings.UserId,
                    this.Context.Guild.Id, artistName);

                if (guildAlsoPlaying != null)
                {
                    footer += "\n";
                    footer += guildAlsoPlaying;
                }

                if (guild.GuildUsers.Count > filteredGuildUsers.Count)
                {
                    var filteredAmount = guild.GuildUsers.Count - filteredGuildUsers.Count;
                    footer += $"\n{filteredAmount} inactive/blocked users filtered";
                }

                this._embed.WithTitle($"Who knows {artistName} in {this.Context.Guild.Name}");

                if (Uri.IsWellFormedUriString(artistUrl, UriKind.Absolute))
                {
                    this._embed.WithUrl(artistUrl);
                }

                this._embedFooter.WithText(footer);
                this._embed.WithFooter(this._embedFooter);

                if (spotifyImageUrl != null)
                {
                    this._embed.WithThumbnailUrl(spotifyImageUrl);
                }

                if (this.Context.InteractionData != null)
                {
                    await this.Context.Channel.SendInteractionMessageAsync(this.Context.InteractionData, embed: this._embed.Build(), type: InteractionMessageType.ChannelMessageWithSource);
                }
                else
                {
                    await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
                }

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
                    await ReplyAsync("Something went wrong while using whoknows. Please let us know as this feature is in beta.");
                }
            }
        }

        [Command("serverartists", RunMode = RunMode.Async)]
        [Summary("Shows top artists for your server")]
        [Alias("sa", "sta", "servertopartists", "server artists")]
        public async Task GuildArtistsAsync(params string[] extraOptions)
        {
            if (this._guildService.CheckIfDM(this.Context))
            {
                await ReplyAsync("This command is not supported in DMs.");
                this.Context.LogCommandUsed(CommandResponse.NotSupportedInDm);
                return;
            }

            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id) ?? ConfigData.Data.Bot.Prefix;
            var guild = await this._guildService.GetGuildAsync(this.Context.Guild.Id);

            var filteredGuildUsers = this._guildService.FilterGuildUsersAsync(guild);

            if (extraOptions.Any() && extraOptions.First() == "help")
            {
                this._embed.WithTitle($"{prfx}serverartists");

                var helpDescription = new StringBuilder();
                helpDescription.AppendLine("Shows the top artists for your server.");
                helpDescription.AppendLine();
                helpDescription.AppendLine("Available time periods: `weekly` and `alltime`");
                helpDescription.AppendLine("Available order options: `plays` and `listeners`");

                this._embed.WithDescription(helpDescription.ToString());

                this._embed.AddField("Examples",
                    $"`{prfx}sa` \n" +
                    $"`{prfx}sa a p` \n" +
                    $"`{prfx}serverartists` \n" +
                    $"`{prfx}serverartists alltime` \n" +
                    $"`{prfx}serverartists listeners weekly`");

                await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
                this.Context.LogCommandUsed(CommandResponse.Help);
                return;
            }

            if (guild.LastIndexed == null)
            {
                await ReplyAsync("This server hasn't been indexed yet.\n" +
                                 $"Please run `{prfx}index` to index this server.");
                this.Context.LogCommandUsed(CommandResponse.IndexRequired);
                return;
            }
            if (guild.LastIndexed < DateTime.UtcNow.AddDays(-60))
            {
                await ReplyAsync("Server index data is out of date, it was last updated over 60 days ago.\n" +
                                 $"Please run `{prfx}index` to re-index this server.");
                this.Context.LogCommandUsed(CommandResponse.IndexRequired);
                return;
            }

            _ = this.Context.Channel.TriggerTypingAsync();

            var serverArtistSettings = new GuildRankingSettings
            {
                ChartTimePeriod = ChartTimePeriod.Weekly,
                OrderType = OrderType.Listeners
            };

            serverArtistSettings = SettingService.SetGuildRankingSettings(serverArtistSettings, extraOptions);

            try
            {
                IReadOnlyList<ListArtist> topGuildArtists;
                var users = filteredGuildUsers.Select(s => s.User).ToList();
                if (serverArtistSettings.ChartTimePeriod == ChartTimePeriod.AllTime)
                {
                    topGuildArtists = await this._whoKnowArtistService.GetTopArtistsForGuild(users, serverArtistSettings.OrderType);
                    this._embed.WithTitle($"Top alltime artists in {this.Context.Guild.Name}");
                }
                else
                {
                    topGuildArtists = await this._whoKnowsPlayService.GetTopWeekArtistsForGuild(users, serverArtistSettings.OrderType);
                    this._embed.WithTitle($"Top weekly artists in {this.Context.Guild.Name}");
                }

                var description = "";
                var footer = "";

                if (serverArtistSettings.OrderType == OrderType.Listeners)
                {
                    footer += "Listeners / Plays - Ordered by listeners\n";
                }
                else
                {
                    footer += "Listeners / Plays - Ordered by plays\n";
                }

                foreach (var artist in topGuildArtists)
                {
                    description += $"`{artist.ListenerCount}` / `{artist.Playcount}` | **{artist.ArtistName}**\n";
                }

                this._embed.WithDescription(description);

                var rnd = new Random();
                var randomHintNumber = rnd.Next(0, 5);
                if (randomHintNumber == 1)
                {
                    footer += $"View specific artist listeners with {prfx}whoknows\n";
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

                if (guild.GuildUsers.Count > filteredGuildUsers.Count)
                {
                    var filteredAmount = guild.GuildUsers.Count - filteredGuildUsers.Count;
                    footer += $"{filteredAmount} inactive/blocked users filtered";
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
                    "Something went wrong while using serverartists. Please report this issue.");
            }
        }


        [Command("affinity", RunMode = RunMode.Async)]
        [Summary("Shows what other users in the same server listen to the same music as you")]
        [Alias("n", "aff", "neighbors")]
        [UsernameSetRequired]
        public async Task AffinityAsync()
        {
            if (this._guildService.CheckIfDM(this.Context))
            {
                await ReplyAsync("This command is not supported in DMs.");
                this.Context.LogCommandUsed(CommandResponse.NotSupportedInDm);
                return;
            }

            var userSettings = await this._userService.GetUserSettingsAsync(this.Context.User);
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id) ?? ConfigData.Data.Bot.Prefix;

            var guild = await this._guildService.GetGuildAsync(this.Context.Guild.Id);
            var filteredGuildUsers = this._guildService.FilterGuildUsersAsync(guild);

            if (guild.LastIndexed == null)
            {
                await ReplyAsync("This server hasn't been indexed yet.\n" +
                                 $"Please run `{prfx}index` to index this server.");
                this.Context.LogCommandUsed(CommandResponse.IndexRequired);
                return;
            }

            if (guild.LastIndexed < DateTime.UtcNow.AddDays(-50))
            {
                await ReplyAsync("Server index data is out of date, it was last updated over 50 days ago.\n" +
                                 $"Please run `{prfx}index` to re-index this server.");
                this.Context.LogCommandUsed(CommandResponse.IndexRequired);
                return;
            }

            _ = this.Context.Channel.TriggerTypingAsync();

            var users = filteredGuildUsers.Select(s => s.User).ToList();
            var neighbors = await this._whoKnowArtistService.GetNeighbors(users, userSettings.UserId);

            var description = new StringBuilder();

            foreach (var neighbor in neighbors.Take(15))
            {
                description.AppendLine($"**[{neighbor.Name}]({Constants.LastFMUserUrl}{neighbor.LastFMUsername})** " +
                                       $"- {neighbor.MatchPercentage:0.0}%");
            }

            var userTitle = await this._userService.GetUserTitleAsync(this.Context);

            this._embed.WithTitle($"Neighbors for {userTitle}");
            this._embed.WithFooter("Experimental command - work in progress");

            this._embed.WithDescription(description.ToString());

            await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());

        }

        private async Task<string> GetArtistOrHelp(string artistValues, User userSettings, string command, string prfx)
        {
            string artist;
            if (!string.IsNullOrWhiteSpace(artistValues))
            {
                if (artistValues.ToLower() == "help")
                {
                    await ReplyAsync(
                        $"Usage: `.fm{command} 'name'`\n" +
                        "If you don't enter any artists name, it will get the info from the artist you're currently listening to.");
                    return null;
                }

                artist = artistValues;
            }
            else
            {
                var recentScrobbles = await this._lastFmService.GetRecentTracksAsync(userSettings.UserNameLastFM, useCache: true);

                if (!recentScrobbles.Success || recentScrobbles.Content == null)
                {
                    this._embed.ErrorResponse(recentScrobbles.Error, recentScrobbles.Message, this.Context);
                    this.Context.LogCommandUsed(CommandResponse.LastFmError);
                    await ReplyAsync("", false, this._embed.Build());
                    return null;
                }

                if (!recentScrobbles.Content.RecentTracks.Track.Any())
                {
                    this._embed.NoScrobblesFoundErrorResponse(userSettings.UserNameLastFM);
                    this.Context.LogCommandUsed(CommandResponse.NoScrobbles);
                    await ReplyAsync("", false, this._embed.Build());
                    return null;
                }

                artist = recentScrobbles.Content.RecentTracks.Track[0].Artist.Text;
            }

            return artist;
        }
    }
}
