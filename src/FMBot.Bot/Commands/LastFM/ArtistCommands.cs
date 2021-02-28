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
using FMBot.Bot.Services.Guild;
using FMBot.Bot.Services.ThirdParty;
using FMBot.Bot.Services.WhoKnows;
using FMBot.Domain;
using FMBot.Domain.Models;
using FMBot.LastFM.Domain.Models;
using FMBot.LastFM.Domain.Types;
using FMBot.LastFM.Services;
using FMBot.Persistence.Domain.Models;
using Interactivity;
using Interactivity.Pagination;
using Interactivity.Selection;

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

        private readonly IPrefixService _prefixService;
        private readonly IUpdateService _updateService;
        private readonly LastFmService _lastFmService;
        private readonly PlayService _playService;
        private readonly SettingService _settingService;
        private readonly SpotifyService _spotifyService;
        private readonly UserService _userService;
        private readonly WhoKnowsService _whoKnowsService;
        private readonly WhoKnowsArtistService _whoKnowArtistService;
        private readonly WhoKnowsPlayService _whoKnowsPlayService;

        private readonly EmbedAuthorBuilder _embedAuthor;
        private readonly EmbedBuilder _embed;
        private readonly EmbedFooterBuilder _embedFooter;

        private InteractivityService Interactivity { get; }

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
                InteractivityService interactivity,
                WhoKnowsService whoKnowsService)
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
            this._whoKnowsService = whoKnowsService;

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

            var artist = await GetArtistOrHelp(artistValues, userSettings, "artist", prfx, null);
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

            var artistCall = await this._lastFmApi.CallApiAsync<ArtistInfoLfmResponse>(queryParams, Call.ArtistInfo);
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
                var guild = await this._guildService.GetFullGuildAsync(this.Context.Guild.Id);

                var usersWithArtist = await this._whoKnowArtistService.GetIndexedUsersForArtist(this.Context, guild.GuildId, artistInfo.Name);
                var filteredUsersWithArtist = WhoKnowsService.FilterGuildUsersAsync(usersWithArtist, guild);

                if (guild.LastIndexed != null && filteredUsersWithArtist.Any())
                {
                    var serverListeners = filteredUsersWithArtist.Count;
                    var serverPlaycount = filteredUsersWithArtist.Sum(a => a.Playcount);
                    var avgServerPlaycount = filteredUsersWithArtist.Average(a => a.Playcount);
                    var serverPlaycountLastWeek = await this._whoKnowArtistService.GetWeekArtistPlaycountForGuildAsync(guild.GuildId, artistInfo.Name);

                    serverStats += $"`{serverListeners}` {StringExtensions.GetListenersString(serverListeners)}";
                    serverStats += $"\n`{serverPlaycount}` total {StringExtensions.GetPlaysString(serverPlaycount)}";
                    serverStats += $"\n`{(int)avgServerPlaycount}` avg {StringExtensions.GetPlaysString((int)avgServerPlaycount)}";
                    serverStats += $"\n`{serverPlaycountLastWeek}` {StringExtensions.GetPlaysString(serverPlaycountLastWeek)} last week";

                    if (usersWithArtist.Count > filteredUsersWithArtist.Count)
                    {
                        var filteredAmount = usersWithArtist.Count - filteredUsersWithArtist.Count;
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
                await this._updateService.CorrectUserArtistPlaycount(userSettings.UserId, artistInfo.Name,
                    artistInfo.Stats.Userplaycount.Value);
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
        [Alias("at", "att", "artisttrack", "artist track", "artist tracks", "artistrack", "artisttoptracks", "artisttoptrack", "favs")]
        [UsernameSetRequired]
        public async Task ArtistTracksAsync([Remainder] string artistValues = null)
        {
            var user = await this._userService.GetUserSettingsAsync(this.Context.User);
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id) ?? ConfigData.Data.Bot.Prefix;

            _ = this.Context.Channel.TriggerTypingAsync();

            var timeSettings = SettingService.GetTimePeriod(artistValues, ChartTimePeriod.AllTime);

            var artist = await GetArtistOrHelp(timeSettings.NewSearchValue, user, "artisttracks", prfx, null);
            if (artist == null)
            {
                this.Context.LogCommandUsed(CommandResponse.NotFound);
                return;
            }

            var queryParams = new Dictionary<string, string>
            {
                {"artist", artist },
                {"username", user.UserNameLastFM },
                {"autocorrect", "1"}
            };
            var artistCall = await this._lastFmApi.CallApiAsync<ArtistInfoLfmResponse>(queryParams, Call.ArtistInfo);
            if (!artistCall.Success)
            {
                this._embed.ErrorResponse(artistCall.Error, artistCall.Message, this.Context);
                await ReplyAsync("", false, this._embed.Build());
                this.Context.LogCommandWithLastFmError(artistCall.Error);
                return;
            }

            var paginationEnabled = false;
            var pages = new List<PageBuilder>();
            var perms = await this._guildService.CheckSufficientPermissionsAsync(this.Context);
            if (perms.ManageMessages)
            {
                paginationEnabled = true;
            }

            var artistInfo = artistCall.Content.Artist;

            var timeDescription = timeSettings.Description.ToLower();
            List<UserTrack> topTracks;
            switch (timeSettings.ChartTimePeriod)
            {
                case ChartTimePeriod.Weekly:
                    topTracks = await this._playService.GetTopTracksForArtist(user.UserId, 7, artistInfo.Name);
                    break;
                case ChartTimePeriod.Monthly:
                    topTracks = await this._playService.GetTopTracksForArtist(user.UserId, 31, artistInfo.Name);
                    break;
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
                var embedTitle = $"Your top {timeDescription} tracks for '{artistInfo.Name}'";
                this._embed.WithTitle(embedTitle);

                var footer = $"{userTitle} has {artistInfo.Stats.Userplaycount} total scrobbles on this artist";
                this._embed.WithFooter(footer);


                var url = $"{Constants.LastFMUserUrl}{user.UserNameLastFM}/library/music/{UrlEncoder.Default.Encode(artistInfo.Name)}";
                if (Uri.IsWellFormedUriString(url, UriKind.Absolute))
                {
                    this._embed.WithUrl(url);
                }

                var amount = paginationEnabled ? 100 : 10;
                amount = topTracks.Count < amount ? topTracks.Count : amount;

                var description = new StringBuilder();
                for (var i = 0; i < topTracks.Count; i++)
                {
                    if (paginationEnabled && (i > 0 && i % 10 == 0 || i == amount - 1))
                    {
                        var page = new PageBuilder()
                            .WithDescription(description.ToString())
                            .WithTitle(embedTitle)
                            .WithFooter(footer);
                        if (Uri.IsWellFormedUriString(url, UriKind.Absolute))
                        {
                            page.WithUrl(url);
                        }

                        pages.Add(page);
                        description = new StringBuilder();
                    }

                    var track = topTracks[i];

                    description.AppendLine($"{i + 1}. **{track.Name}** ({track.Playcount} plays)");
                }

                this._embed.WithDescription(description.ToString());

            }

            if (paginationEnabled)
            {
                var paginator = new StaticPaginatorBuilder()
                    .WithPages(pages)
                    .WithFooter(PaginatorFooter.PageNumber)
                    .WithEmotes(DiscordConstants.PaginationEmotes)
                    .Build();

                _ = this.Interactivity.SendPaginatorAsync(paginator, this.Context.Channel, TimeSpan.FromMinutes(1));
            }
            else
            {
                await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
            }

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

            var artist = await GetArtistOrHelp(artistValues, user, "artistalbums", prfx, null);
            if (artist == null)
            {
                this.Context.LogCommandUsed(CommandResponse.NotFound);
                return;
            }

            _ = this.Context.Channel.TriggerTypingAsync();

            var queryParams = new Dictionary<string, string>
            {
                {"artist", artist },
                {"username", user.UserNameLastFM },
                {"autocorrect", "1"}
            };
            var artistCall = await this._lastFmApi.CallApiAsync<ArtistInfoLfmResponse>(queryParams, Call.ArtistInfo);
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
            var user = await this._userService.GetUserSettingsAsync(this.Context.User);
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id) ?? ConfigData.Data.Bot.Prefix;

            _ = this.Context.Channel.TriggerTypingAsync();

            var userSettings = await this._settingService.GetUser(artistValues, user, this.Context);

            var artist = await GetArtistOrHelp(userSettings.NewSearchValue, user, "artistplays", prfx, null);
            if (artist == null)
            {
                this.Context.LogCommandUsed(CommandResponse.NotFound);
                return;
            }

            var queryParams = new Dictionary<string, string>
            {
                {"artist", artist },
                {"username", userSettings.UserNameLastFm },
                {"autocorrect", "1"}
            };
            var artistCall = await this._lastFmApi.CallApiAsync<ArtistInfoLfmResponse>(queryParams, Call.ArtistInfo);
            if (!artistCall.Success)
            {
                this._embed.ErrorResponse(artistCall.Error, artistCall.Message, this.Context);
                await ReplyAsync("", false, this._embed.Build());
                this.Context.LogCommandWithLastFmError(artistCall.Error);
                return;
            }

            var artistInfo = artistCall.Content.Artist;

            var userTitle = await this._userService.GetUserTitleAsync(this.Context);

            var reply =
                $"**{userSettings.DiscordUserName.FilterOutMentions()}{userSettings.UserType.UserTypeToIcon()}** has `{artistInfo.Stats.Userplaycount}` {StringExtensions.GetPlaysString(artistInfo.Stats.Userplaycount)} for **{artistInfo.Name.FilterOutMentions()}**";

            if (!userSettings.DifferentUser && user.LastUpdated != null)
            {
                var playsLastWeek =
                    await this._playService.GetWeekArtistPlaycountAsync(userSettings.UserId, artistInfo.Name);
                if (playsLastWeek != 0)
                {
                    reply += $" (`{playsLastWeek}` last week)";
                }
            }

            await this.Context.Channel.SendMessageAsync(reply);
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

                this._embed.AddField("Pagination",
                    "This command supports pagination. To enable this you need to make sure the bot has the `Manage Messages` permission (server-wide).");

                await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
                this.Context.LogCommandUsed(CommandResponse.Help);
                return;
            }

            _ = this.Context.Channel.TriggerTypingAsync();

            var timePeriodString = extraOptions;

            var amountString = extraOptions;

            var timeSettings = SettingService.GetTimePeriod(timePeriodString);
            var userSettings = await this._settingService.GetUser(extraOptions, user, this.Context);
            var amount = SettingService.GetAmount(amountString);

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

            var artistsString = amount == 1 ? "artist" : "artists";

            this._embedAuthor.WithIconUrl(this.Context.User.GetAvatarUrl());
            this._embedAuthor.WithName($"Top {amount} {timeSettings.Description.ToLower()} {artistsString} for {userTitle}");
            this._embedAuthor.WithUrl($"{Constants.LastFMUserUrl}{userSettings.UserNameLastFm}/library/artists?{timeSettings.UrlParameter}");

            amount = paginationEnabled ? 100 : amount;

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

                    var footer = $"{artists.TotalItems} different artists in this time period";

                    var rnd = new Random();
                    if (rnd.Next(0, 2) == 1)
                    {
                        footer += $"\nWant pagination? Enable the 'Manage Messages' permission for .fmbot.";
                    }

                    amount = artists.Content.Count;

                    for (var i = 0; i < amount; i++)
                    {
                        if (paginationEnabled && (i > 0 && i % 10 == 0 || i == amount - 1))
                        {
                            pages.Add(new PageBuilder().WithDescription(description).WithAuthor(this._embedAuthor).WithFooter(footer));
                            description = "";
                        }

                        var artist = artists.Content[i];

                        if (artists.Count() > 10 && !paginationEnabled)
                        {
                            description += $"{i + 1}. **{artist.Name}** ({artist.PlayCount} plays) \n";
                        }
                        else
                        {
                            description += $"{i + 1}. **[{artist.Name}]({artist.Url})** ({artist.PlayCount} plays) \n";
                        }
                    }

                    this._embedFooter.WithText(footer);
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

                    var footer = $"{artists.Count} different artists in this time period";

                    amount = artists.Count < amount ? artists.Count : amount;
                    for (var i = 0; i < amount; i++)
                    {
                        if (paginationEnabled && (i > 0 && i % 10 == 0 || i == amount - 1))
                        {
                            pages.Add(new PageBuilder().WithDescription(description).WithAuthor(this._embedAuthor).WithFooter(footer));
                            description = "";
                        }

                        var artist = artists[i];

                        description += $"{i + 1}. **{artist.Name}** ({artist.Playcount} {StringExtensions.GetPlaysString(artist.Playcount)}) \n";
                    }

                    this._embedFooter.WithText(footer);
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

                    _ = this.Interactivity.SendPaginatorAsync(paginator, this.Context.Channel, TimeSpan.FromSeconds(DiscordConstants.PaginationTimeoutInSeconds));
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
                await ReplyAsync("Unable to show Last.fm info due to an internal error.");
            }
        }

        [Command("lazypaginator")]
        public async Task LazyPaginatorAsync()
        {
            try
            {
                var perms = await this._guildService.CheckSufficientPermissionsAsync(this.Context);
                if (!perms.ManageMessages)
                {
                    await ReplyAsync(
                        "I'm missing the 'mng messages' permission in this server, so I can't post a chart.");
                    this.Context.LogCommandUsed(CommandResponse.NoPermission);
                    return;
                }

                var pages = new PageBuilder[] {
                    new PageBuilder().WithTitle("I"),
                    new PageBuilder().WithTitle("am"),
                    new PageBuilder().WithTitle("cool"),
                    new PageBuilder().WithTitle(":sunglasses:"),
                    new PageBuilder().WithText("I am cool :crown:")
                };

                var paginator = new StaticPaginatorBuilder()
                    .WithPages(pages)
                    .WithFooter(PaginatorFooter.PageNumber)
                    .WithEmotes(new Dictionary<IEmote, PaginatorAction>
                    {
                        { new Emoji("⏮️"), PaginatorAction.SkipToStart},
                        { new Emoji("⬅️"), PaginatorAction.Backward},
                        { new Emoji("➡️"), PaginatorAction.Forward},
                        { new Emoji("⏭️"), PaginatorAction.SkipToEnd}
                    })
                    .Build();

                await this.Interactivity.SendPaginatorAsync(paginator, this.Context.Channel, TimeSpan.FromMinutes(2));
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
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

            var timePeriodString = extraOptions;

            var timeType = SettingService.GetTimePeriod(
                timePeriodString,
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
                    var errorReply = $"Error while attempting taste comparison: \n\n" +
                                     $"Please enter a valid user to compare your top artists to.\n" +
                                     $"Example: `{prfx}taste lastfmusername` or `{prfx}taste @user`.\n" +
                                     $"Make sure the user is registered in .fmbot or has a public Last.fm profile.";


                    await ReplyAsync(errorReply);

                    this.Context.LogCommandUsed(CommandResponse.WrongInput);
                    return;
                }
                if (lastfmToCompare.ToLower() == userSettings.UserNameLastFM.ToLower())
                {
                    var errorReply =
                        $"Error while attempting taste comparison: \n\n" +
                        $"You can't compare your own taste with yourself. For viewing your top artists, use `{prfx}topartists`\n" +
                        $"Please enter a different last.fm username or mention another user.";

                    await ReplyAsync(errorReply);

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

            try
            {
                var guildTask = this._guildService.GetFullGuildAsync(this.Context.Guild.Id);

                _ = this.Context.Channel.TriggerTypingAsync();

                var userSettings = await this._userService.GetUserSettingsAsync(this.Context.User);

                var artistQuery = await GetArtistOrHelp(artistValues, userSettings, "whoknows", prfx, null);
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

                    var artistCall = await this._lastFmApi.CallApiAsync<ArtistInfoLfmResponse>(queryParams, Call.ArtistInfo);

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
                    if (userPlaycount.HasValue)
                    {
                        await this._updateService.CorrectUserArtistPlaycount(userSettings.UserId, artistCall.Content.Artist.Name,
                            userPlaycount.Value);
                    }
                }

                var guild = await guildTask;

                var currentUser = await this._indexService.GetOrAddUserToGuild(guild, await this.Context.Guild.GetUserAsync(userSettings.DiscordUserId), userSettings);

                if (!guild.GuildUsers.Select(s => s.UserId).Contains(userSettings.UserId))
                {
                    guild.GuildUsers.Add(currentUser);
                }

                await this._indexService.UpdateUserName(currentUser, await this.Context.Guild.GetUserAsync(userSettings.DiscordUserId));

                var usersWithArtist = await this._whoKnowArtistService.GetIndexedUsersForArtist(this.Context, guild.GuildId, artistName);

                if (userPlaycount != 0)
                {
                    usersWithArtist = WhoKnowsService.AddOrReplaceUserToIndexList(usersWithArtist, currentUser, artistName, userPlaycount);
                }

                var filteredUsersWithArtist = WhoKnowsService.FilterGuildUsersAsync(usersWithArtist, guild);

                CrownModel crownModel = null;
                if (guild.CrownsDisabled != true && filteredUsersWithArtist.Count >= 1)
                {
                    crownModel =
                        await this._crownService.GetAndUpdateCrownForArtist(filteredUsersWithArtist, guild, artistName);
                }

                var serverUsers = WhoKnowsService.WhoKnowsListToString(filteredUsersWithArtist, userSettings.UserId, PrivacyLevel.Server, crownModel);
                if (filteredUsersWithArtist.Count == 0)
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

                if (filteredUsersWithArtist.Any() && filteredUsersWithArtist.Count > 1)
                {
                    var serverListeners = filteredUsersWithArtist.Count;
                    var serverPlaycount = filteredUsersWithArtist.Sum(a => a.Playcount);
                    var avgServerPlaycount = filteredUsersWithArtist.Average(a => a.Playcount);

                    footer += $"\n{serverListeners} {StringExtensions.GetListenersString(serverListeners)} - ";
                    footer += $"{serverPlaycount} total {StringExtensions.GetPlaysString(serverPlaycount)} - ";
                    footer += $"{(int)avgServerPlaycount} avg {StringExtensions.GetPlaysString((int)avgServerPlaycount)}";
                }

                var guildAlsoPlaying = await this._whoKnowsPlayService.GuildAlsoPlayingArtist(userSettings.UserId,
                    this.Context.Guild.Id, artistName);

                if (guildAlsoPlaying != null)
                {
                    footer += "\n";
                    footer += guildAlsoPlaying;
                }

                if (usersWithArtist.Count > filteredUsersWithArtist.Count)
                {
                    var filteredAmount = usersWithArtist.Count - filteredUsersWithArtist.Count;
                    footer += $"\n{filteredAmount} inactive/blocked users filtered";
                }

                this._embed.WithTitle($"{artistName} in {this.Context.Guild.Name}");

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
                    await ReplyAsync("Something went wrong while using whoknows.");
                }
            }
        }

        [Command("globalwhoknows", RunMode = RunMode.Async)]
        [Summary("Shows what other users listen to the same artist on .fmbot")]
        [Alias("gw", "gwk", "globalwhoknows artist")]
        [UsernameSetRequired]
        [GuildOnly]
        public async Task GlobalWhoKnowsAsync([Remainder] string artistValues = null)
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

            try
            {
                var guildTask = this._guildService.GetFullGuildAsync(this.Context.Guild.Id);
                _ = this.Context.Channel.TriggerTypingAsync();

                var userSettings = await this._userService.GetUserSettingsAsync(this.Context.User);

                var artistQuery = await GetArtistOrHelp(artistValues, userSettings, "whoknows", prfx, null);
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

                    var artistCall = await this._lastFmApi.CallApiAsync<ArtistInfoLfmResponse>(queryParams, Call.ArtistInfo);

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
                    if (userPlaycount.HasValue)
                    {
                        await this._updateService.CorrectUserArtistPlaycount(userSettings.UserId, artistCall.Content.Artist.Name,
                            userPlaycount.Value);
                    }
                }

                var usersWithArtist = await this._whoKnowArtistService.GetGlobalUsersForArtists(this.Context, artistName);

                if (userPlaycount != 0 && this.Context.Guild != null)
                {
                    var discordGuildUser = await this.Context.Guild.GetUserAsync(userSettings.DiscordUserId);
                    var guildUser = new GuildUser
                    {
                        UserName = discordGuildUser != null ? discordGuildUser.Nickname ?? discordGuildUser.Username : userSettings.UserNameLastFM,
                        User = userSettings
                    };
                    usersWithArtist = WhoKnowsService.AddOrReplaceUserToIndexList(usersWithArtist, guildUser, artistName, userPlaycount);
                }

                var guild = await guildTask;

                var filteredUsersWithArtist = await this._whoKnowsService.FilterGlobalUsersAsync(usersWithArtist);

                filteredUsersWithArtist =
                    WhoKnowsService.ShowGuildMembersInGlobalWhoKnowsAsync(filteredUsersWithArtist, guild.GuildUsers.ToList());

                var serverUsers = WhoKnowsService.WhoKnowsListToString(filteredUsersWithArtist, userSettings.UserId, PrivacyLevel.Global);
                if (filteredUsersWithArtist.Count == 0)
                {
                    serverUsers = "Nobody that uses .fmbot has listened to this artist.";
                }

                this._embed.WithDescription(serverUsers);

                var userTitle = await this._userService.GetUserTitleAsync(this.Context);
                var footer = $"Global WhoKnows artist requested by {userTitle}";

                if (filteredUsersWithArtist.Any() && filteredUsersWithArtist.Count > 1)
                {
                    var globalListeners = filteredUsersWithArtist.Count;
                    var globalPlaycount = filteredUsersWithArtist.Sum(a => a.Playcount);
                    var avgPlaycount = filteredUsersWithArtist.Average(a => a.Playcount);

                    footer += $"\n{globalListeners} {StringExtensions.GetListenersString(globalListeners)} - ";
                    footer += $"{globalPlaycount} total {StringExtensions.GetPlaysString(globalPlaycount)} - ";
                    footer += $"{(int)avgPlaycount} avg {StringExtensions.GetPlaysString((int)avgPlaycount)}";
                }

                var guildAlsoPlaying = await this._whoKnowsPlayService.GuildAlsoPlayingArtist(userSettings.UserId,
                    this.Context.Guild.Id, artistName);

                if (guildAlsoPlaying != null)
                {
                    footer += "\n";
                    footer += guildAlsoPlaying;
                }

                if (userSettings.PrivacyLevel != PrivacyLevel.Global)
                {
                    footer += $"\nYou are currently not globally visible - use '{prfx}privacy global' to enable.";
                }

                this._embed.WithTitle($"{artistName} globally");

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
                    await ReplyAsync("Something went wrong while using global whoknows.");
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
            var guild = await this._guildService.GetFullGuildAsync(this.Context.Guild.Id);

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

            var guild = await this._guildService.GetFullGuildAsync(this.Context.Guild.Id);
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

        private async Task<string> GetArtistOrHelp(string artistValues, User userSettings, string command, string prfx, string alternativeLastFmUserName)
        {
            string artist;
            if (!string.IsNullOrWhiteSpace(artistValues))
            {
                if (artistValues.ToLower() == "help")
                {
                    var helpText =
                        $"Usage: `{prfx}{command} 'name'`\n" +
                        "If you don't enter any artists name, it will get the info from the artist you're currently listening to.";

                    await this.Context.Channel.SendMessageAsync(helpText);

                    return null;
                }

                artist = artistValues;
            }
            else
            {
                Response<RecentTrackList> recentTracks;

                if (string.IsNullOrWhiteSpace(alternativeLastFmUserName))
                {
                    string sessionKey = null;
                    if (!string.IsNullOrEmpty(userSettings.SessionKeyLastFm))
                    {
                        sessionKey = userSettings.SessionKeyLastFm;
                    }

                    if (userSettings.LastIndexed == null)
                    {
                        _ = this._indexService.IndexUser(userSettings);
                        recentTracks = await this._lastFmService.GetRecentTracksAsync(userSettings.UserNameLastFM, useCache: true, sessionKey: sessionKey);
                    }
                    else
                    {
                        recentTracks = await this._updateService.UpdateUserAndGetRecentTracks(userSettings);
                    }
                }
                else
                {
                    recentTracks = await this._lastFmService.GetRecentTracksAsync(alternativeLastFmUserName, useCache: true);
                }

                userSettings = await this._userService.GetUserSettingsAsync(this.Context.User);

                if (await ErrorService.RecentScrobbleCallFailedReply(recentTracks, userSettings.UserNameLastFM, this.Context))
                {
                    return null;
                }

                artist = recentTracks.Content.RecentTracks[0].ArtistName;
            }

            return artist;
        }
    }
}
